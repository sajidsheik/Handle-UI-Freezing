--app.UseMiddleware<LimioCallLoggingMiddleware>();

public class ExternalCallLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;
    // Add other routes as needed
    private static readonly string[] RoutesToLog = ["/api/purchase/External/Cart"];
    // To make sure that we don't log excessively large bodies (10 KB)
    private const int MaxRequestBodySize = 10 * 1024;
    
    /// <summary>
    /// Initiation Constructor
    /// </summary>
    /// <param name="next"></param>
    /// <param name="scopeFactory"></param>
    public ExternalCallLoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Invokes the middleware to log External API calls
    /// </summary>
    /// <param name="context"></param>
    /// <param name="logContext"></param>
    /// <returns></returns>
    public async Task InvokeAsync(HttpContext context, LogExternalCallContext logContext)
    {
        // Only log specific routes - PERFORMANCE CRITICAL
        if (!ShouldLogRoute(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Set the CorrelationId from HttpContext
        logContext.Log.CorrelationId = GetCorrelationId(context);

        var stopwatch = Stopwatch.StartNew();
        string? errorMessage = null;
        string? stackTrace = null;
        string? validationErrors = null;
        int statusCode = 200;
        string? requestBody = null;
        string? actionName = null;
        ApiFailureReason? failureReason = null;
        
        // Store the original response body stream
        var originalResponseBodyStream = context.Response.Body;
        
        try
        {
            context.Request.EnableBuffering();

            // Read the request body with size limit
            if (context.Request.ContentLength > 0)
            {
                if (context.Request.ContentLength <= MaxRequestBodySize)
                {
                    using var reader = new StreamReader(
                        context.Request.Body,
                        encoding: System.Text.Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: false,
                        leaveOpen: true);

                    requestBody = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;

                    // Extract LicenseKey from CartRequest
                    if (!string.IsNullOrEmpty(requestBody))
                    {
                        try
                        {
                            var CartRequest = JsonConvert.DeserializeObject<CartRequest>(requestBody);
                            
                            // Re-serialize to compact format for consistent storage
                            requestBody = JsonConvert.SerializeObject(CartRequest);
                            
                            if (CartRequest?.LicenseKey != null && Guid.TryParse(CartRequest.LicenseKey, out var licenseKey))
                            {
                                logContext.Log.LicenseKey = licenseKey;
                            }
                        }
                        catch
                        {
                            // Keep original requestBody if deserialization fails
                        }
                    }
                }
                else
                {
                    requestBody = $"[Request body too large: {context.Request.ContentLength} bytes]";
                }
            }

            // Create a new memory stream to capture the response
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;
            
            await _next(context);
            
            // Get the action name after the request is processed
            var endpoint = context.GetEndpoint();
            if (endpoint != null)
            {
                var controllerActionDescriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
                if (controllerActionDescriptor != null)
                {
                    actionName = $"{controllerActionDescriptor.ControllerName}.{controllerActionDescriptor.ActionName}";
                }
            }
            
            statusCode = context.Response.StatusCode;

            // Read validation errors from response body if status is 400
            if (statusCode == 400)
            {
                failureReason = ApiFailureReason.VALIDATION_ERROR;
                
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(responseBodyStream, leaveOpen: true);
                var responseBody = await reader.ReadToEndAsync();
                
                if (!string.IsNullOrEmpty(responseBody))
                {
                    try
                    {
                        // Deserialize the ErrorResponse to extract ErrorIds
                        var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(responseBody);
                        if (errorResponse?.Errors != null && errorResponse.Errors.Count > 0)
                        {
                            validationErrors = JsonConvert.SerializeObject(errorResponse.Errors);
                        }
                    }
                    catch
                    {
                        // If deserialization fails, store the raw response body
                        validationErrors = responseBody;
                    }
                }
            }

            // Copy the response body back to the original stream
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            await responseBodyStream.CopyToAsync(originalResponseBodyStream);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            stackTrace = ex.StackTrace;
            statusCode = 500;
            failureReason = ApiFailureReason.CODE_EXCEPTION;
            throw; // Re-throw the exception after logging
        }
        finally
        {
            // Restore the original response body stream
            context.Response.Body = originalResponseBodyStream;
            
            stopwatch.Stop();

            // Check for External API failure
            if (logContext.Log.ExternalResponseCode.HasValue && logContext.Log.ExternalResponseCode != 200)
            {
                failureReason = ApiFailureReason.External_API_FAILURE;
            }

            // Fill LogExternalCall
            logContext.Log.ApiName = actionName ?? context.Request.Path;
            logContext.Log.ApiRequestBody = requestBody;
            logContext.Log.ApiStatus = statusCode >= 200 && statusCode < 300 ? "Success" : "Failed";
            logContext.Log.ApiErrorMessage = errorMessage;
            logContext.Log.ApiValidationErrors = validationErrors;
            logContext.Log.ApiFailureReason = failureReason?.ToString();
            
            // For 500 errors, combine error message and stack trace
            if (statusCode == 500 && !string.IsNullOrEmpty(stackTrace))
            {
                logContext.Log.ApiErrorMessage = $"{errorMessage}\n\nStack Trace:\n{stackTrace}";
            }

            // Fire-and-forget save
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var ExternalLoggerDataAccess = scope.ServiceProvider.GetRequiredService<IExternalCallLoggerDataAccess>();
                try
                {
                    await ExternalLoggerDataAccess.AddExternalCallLogs(logContext.Log);
                }
                catch(Exception ex)
                {
                    // adding to system level logs but if we are not seeing the call we know that 
                    Debug.WriteLine($"Failed to log External call: {ex}");
                }
            });
        }
    }

    /// <summary>
    /// Extracts CorrelationId from HttpContext
    /// </summary>
    private static Guid GetCorrelationId(HttpContext context)
    {
        // Try to get from HttpContext.Items (set by Serilog CorrelationId enricher)
        if (context.Items.TryGetValue("CorrelationIdEnricher+CorrelationId", out var correlationIdObj))
        {
            if (correlationIdObj is string correlationIdString && Guid.TryParse(correlationIdString, out var correlationId))
            {
                return correlationId;
            }
            
            if (correlationIdObj is Guid guidValue)
            {
                return guidValue;
            }
        }

        // Fallback: generate a new GUID if not found
        return Guid.NewGuid();
    }

    private bool ShouldLogRoute(PathString path)
    {
        return RoutesToLog.Any(r => path.StartsWithSegments(r, StringComparison.OrdinalIgnoreCase));
    }
}

