# Handle-UI-Freezing
Techniques to handle large volumes of data on UI.

Eg: In the case of an Excel web add-in in Microsoft O365 applications, we export and import large datasets on which
we perform certain actions to create the rows and columns on the portal.
Solved the problem using the processing of the set/batch of data at a time using chunking and delaying it using the setTimeout, which unblocks the main thread to asynchronous process
the data

# Redis Optimization using the HashSetString instead of SetString
