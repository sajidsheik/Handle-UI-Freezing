# Handle-UI-Freezing
Techniques to handle large volumes of data on UI.

Eg, In the case of an Excel web add-in in Microsoft O365 applications, we export and import large datasets on which
We perform certain actions to create the rows and columns on the portal.
Solved the problem using the processing of the set/batch of data at a time using chunking and delaying it using the setTimeout, which unblocks the main thread to asynchronously process the data

# Redis Optimization using the HashSetString instead of SetString
Reduced the network calls to O(N) to O(1)
Faster lookups
Reduced the decryption of the key values for each Key value lookup

# Observability of the entire life cycle of request and response in an application using customMiddleware and Scoped Object
Getting the entire observability of the life cycle of a request in an ASP.NET Core API application
