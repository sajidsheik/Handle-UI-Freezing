-- Example of making 
-- Reduced Time Complexity of external calls to Redis from O(N) to O(1)
-- Extendable solution like if keys size grows the round of calls increases

-- if the latency of each key increase i would introduce the batching concept of get the keys based on the id'server

changed data structure
-- Key -- value pairs

-- Example -- 
			  Mapping-1 --> Json Object
			  Mapping-2 --> Json Object

-- Key -- Field -- values
              Mapping 
			           1 -> Json Object
					   2 -> Json Object
					   
Reading the Keys

private async Task<List<T>> GetTableFromRedis<T>(string tableStorageKey)
{
    var objectList = new List<T>();

    try
    {
        var db = redisConnection.GetDatabase();
        var server = redisConnection.GetServer(redisConnection.GetEndPoints()[0]);
        var instance = configuration.GetSection(InterstitialConstants.RedisInstanceConfiguration).Value;
        foreach (var key in server.Keys(pattern: $"{instance}{tableStorageKey}:*"))
        {
            var keyType = await db.KeyTypeAsync(key);
            if (keyType == RedisType.Hash)
            {
                var value = await db.HashGetAsync(key, "data");

                if (value.IsNullOrEmpty)
                {
                    continue;
                }

                var tableObject = JsonSerializer.Deserialize<T>(value!, cacheJsonOptions);
                if (tableObject != null)
                {
                    objectList.Add(tableObject);
                }

            }
            else if (keyType == RedisType.String)
            {
                var json = await db.StringGetAsync(key);
                if (!json.IsNullOrEmpty)
                {
                    var tableObject = JsonSerializer.Deserialize<T>(json!, cacheJsonOptions);
                    if (tableObject != null)
                    {
                        objectList.Add(tableObject);
                    }
                }
            }
        }
        return objectList;
    }
    catch (Exception e)
    {
        logger.LogError("Error while reading from cache: {message}", e.Message);
        return objectList;
    }
}
  
  private async Task<T?> GetDataFromRedisByKey<T>(string tableStorageKey, string fieldKey)
  {
      try
      {
          var db = redisConnection.GetDatabase();

          var instance = configuration
              .GetSection(InterstitialConstants.RedisInstanceConfiguration)
              .Value;

          var redisKey = $"{instance}{tableStorageKey}";

          var value = await db.HashGetAsync(redisKey, fieldKey);

          if (value.IsNullOrEmpty)
          {
              return default;
          }

          return JsonSerializer.Deserialize<T>(value!, cacheJsonOptions);
      }
      catch (Exception e)
      {
          logger.LogError("Error while reading from cache: {message}", e.Message);
          return default;
      }
  }
  
  
Setting the Keys difference
  
 private async Task SetCacheEntryAsync<T>(string tableKey, string fieldKey, T value)
 {
     var db = redisConnection.GetDatabase();
     var redisKey = $"{configuration.GetSection(InterstitialConstants.RedisInstanceConfiguration).Value}{tableKey}";
     var json = JsonSerializer.Serialize(value, cacheJsonOptions);

     await db.HashSetAsync(redisKey, fieldKey, json);
 }
 
 await distributedCache.SetStringAsync(
    redisKey,
    JsonSerializer.Serialize(data));
	
// Setting the bulk Keys

Run this in for loop
 await distributedCache.SetStringAsync(
    redisKey,
    JsonSerializer.Serialize(data));
	
// Setting the bulk keys

 private async Task SetCacheEntriesAsync<T>(
 string tableKey,
 IEnumerable<T> items,
 Func<T, string> fieldSelector)
 {
     var db = redisConnection.GetDatabase();

     var redisKey = $"{configuration.GetSection(InterstitialConstants.RedisInstanceConfiguration).Value}{tableKey}";

     var entries = items
         .Select(x => new HashEntry(
             fieldSelector(x),
             JsonSerializer.Serialize(x, cacheJsonOptions)))
         .ToArray();

     await db.HashSetAsync(redisKey, entries);
 }