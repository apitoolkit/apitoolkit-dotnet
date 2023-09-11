<p>
<img src="https://apitoolkit.io/assets/img/logo-full.svg" alt="APIToolkit" width="250px" />
</p>

[![Build Status](https://github.com/apitoolkit/apitoolkit-dotnet/workflows/.NET/badge.svg)](https://github.com/apitoolkit/apitoolkit-dotnet1/actions?query=workflow%3ACI) [![NuGet](https://img.shields.io/nuget/v/ApiToolkit.Net.svg)](https://nuget.org/packages/ApiToolkit.Net) [![Nuget](https://img.shields.io/nuget/dt/ApiToolkit.Net.svg)](https://nuget.org/packages/ApiToolkit.Net)


## APIToolkit .NET SDK


APIToolkit is a lightweight middleware that helps Engineering teams build & maintain REST based APIs that their customers love.


## Installation

Run the following command to install the package into your .NET application:

```sh
dotnet add package ApiToolkit.Net
```

Now you can initialize APIToolkit in your application's entry point (eg Program.cs)

```csharp
var config = new Config
{
    Debug = true, # Set debug flags to false in production
    ApiKey = "{Your_APIKey}"
};
var client = await APIToolkit.NewClientAsync(config);

# Register the middleware to use the initialized client
app.Use(async (context, next) =>
{
    var apiToolkit = new APIToolkit(next, client);
    await apiToolkit.InvokeAsync(context);
});

# app.UseEndpoint(..) 
# other middlewares and logic
# ...
```
Please make sure the apitoolkit middleware is added before UseEndpoint and other middlewares are initialized

The field `{Your_APIKey}` should be replaced with the api key which you generated from the apitoolkit dashboard.
In practice, you would set this field using 


## Redacting/Masking fields

If you have fields which are too sensitive and should not be sent to APIToolkit servers, you can mark those fields to be redacted either via the APIToolkit dashboard, or via this client SDK. Redacting fields via the SDK means that those fields never leave your servers in the first place, compared to redacting it via the APIToolkit dashboard, which would redact the fields on the edge before further processing. But then the data still needs to be transported from your servers before they are redacted.

To mark a field for redacting via this SDK, you simply need to provide additional arguments to the APIToolkitService with the paths to the fields that should be redacted. There are 3 potential arguments which you can provide to configure what gets redacted.
- `RedactHeaders`:  A list of HTTP header keys which should be redacted before data is sent out. eg COOKIE(redacted by default), CONTENT-TYPE, etc
- `RedactRequestBody`: A list of JSONpaths which will be redacted from the request body, if the request body is a valid json.
- `RedactResponseBody`: A list of JSONpaths which will be redacted from the response body, if the response body is a valid json.

Examples of valid jsonpaths would be:

`$.store.book`: Will replace the books field inside the store object with the string [CLIENT_REDACTED]
`$.store.books[*].author`: Will redact the author field in all the objects in the books list, inside the store object.

For more examples and introduction to json path, please take a look at: [https://support.smartbear.com/alertsite/docs/monitors/api/endpoint/jsonpath.html](https://support.smartbear.com/alertsite/docs/monitors/api/endpoint/jsonpath.html)


Here's an example of what your configuration in your entry point (Program.cs) would look like with the redacted fields configured:
```csharp
var config = new Config
{
    Debug = true, # Set debug flags to false in production
    ApiKey = "{Your_APIKey}",
    RedactHeaders = new List<string> { "HOST", "CONTENT-TYPE" },
    RedactRequestBody = new List<string> { "$.password", "$.payment.credit_cards[*].cvv", "$.user.addresses[*]" },
    RedactResponseBody = new List<string> { "$.title", "$.store.books[*].author" }
};
var client = await APIToolkit.NewClientAsync(config);

# Register the middleware to use the initialized client
app.Use(async (context, next) =>
{
    var apiToolkit = new APIToolkit(next, client);
    await apiToolkit.InvokeAsync(context);

```


It is important to note that while the `RedactHeaders` config field accepts a list of headers(case insensitive), 
the `RedactRequestBody` and `RedactResponseBody` expect a list of JSONPath strings as arguments.

The choice of JSONPath was selected to allow you have great flexibility in describing which fields within your responses are sensitive.
Also note that these list of items to be redacted will be aplied to all endpoint requests and responses on your server.
To learn more about jsonpath to help form your queries, please take a look at this cheatsheet:
[https://lzone.de/cheat-sheet/JSONPath](https://lzone.de/cheat-sheet/JSONPath)
