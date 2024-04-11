<div align="center">

![APItoolkit's Logo](https://github.com/apitoolkit/.github/blob/main/images/logo-white.svg?raw=true#gh-dark-mode-only)
![APItoolkit's Logo](https://github.com/apitoolkit/.github/blob/main/images/logo-black.svg?raw=true#gh-light-mode-only)

## .NET SDK

[![NuGet](https://img.shields.io/badge/APItoolkit-sdk-0068ff)](https://github.com/topics/apitoolkit-sdk) [![Build Status](https://github.com/apitoolkit/apitoolkit-dotnet/workflows/.NET/badge.svg)](https://github.com/apitoolkit/apitoolkit-dotnet1/actions?query=workflow%3ACI) [![NuGet](https://img.shields.io/nuget/v/ApiToolkit.Net.svg)](https://nuget.org/packages/ApiToolkit.Net) [![Nuget](https://img.shields.io/nuget/dt/ApiToolkit.Net.svg)](https://nuget.org/packages/ApiToolkit.Net)

APItoolkit is an end-to-end API and web services management toolkit for engineers and customer support teams. To integrate `.Net` web services with APItoolkit, you need to use this SDK to monitor incoming traffic, aggregate the requests, and then deliver them to the APItoolkit's servers.

</div>

---

## Table of Contents

- [Installation](#installation)
- [Configuration](#configuration)
- [Redacting Fields](#redacting-fields)
  - [JSONPath Example](#jsonpath-example)
  - [Configuration Example](#configuration-example)
- [Contributing and Help](#contributing-and-help)
- [License](#license)

---

## Installation

Kindly run the following command to install the package:

```sh
dotnet add package ApiToolkit.Net
```

## Configuration

Now you can initialize APItoolkit in your application's entry point (e.g `Program.cs`) like so:

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

> [!NOTE]
> 
> Please make sure the APItoolkit middleware is added before `UseEndpoint` and other middleware are initialized.

> [!IMPORTANT]
> 
> The `{Your_APIKey}` field should be replaced with the API key generated from the APItoolkit dashboard. 


## Redacting Fields

If you have fields that are sensitive and should not be sent to APItoolkit servers, you can mark those fields to be redacted in two ways:
- This client SDK (the fields will never leave your servers in the first place).
- The APItoolkit dashboard (the fields will be transported from your servers first and then redacted on the edge before further processing).

To mark a field for redacting via this SDK, you need to provide additional arguments to the `APIToolkitService` with the paths to the fields that should be redacted. There are three (3) potential arguments that you can provide to configure what gets redacted.
1. `RedactHeaders`:  A list of HTTP header keys that should be redacted before data is sent out (e.g., `COOKIE` (redacted by default), `CONTENT-TYPE`, etc.).
2. `RedactRequestBody`: A list of JSONPaths that should be redacted from the request body (if the request body is a valid JSON).
3. `RedactResponseBody`: A list of JSONPaths that should be redacted from the response body (if the response body is a valid JSON).

### JSONPath Example

Given the following JSON object:

```JSON
{
    "store": {
        "books": [
            {
                "category": "reference",
                "author": "Nigel Rees",
                "title": "Sayings of the Century",
                "price": 8.95
            },
            {
                "category": "fiction",
                "author": "Evelyn Waugh",
                "title": "Sword of Honour",
                "price": 12.99
            },
            ...
        ],
        "bicycle": {
            "color": "red",
            "price": 19.95
        }
    },
    ...
}
```

Examples of valid JSONPaths would be:

- `$.store.books`: APItoolkit will replace the `books` field inside the store object with the string `[CLIENT_REDACTED]`.
- `$.store.books[*].author`: APItoolkit will replace the `author` field in all the objects in the `books` list inside the `store` object with the string `[CLIENT_REDACTED]`.

For more examples and an introduction to JSONPath, please take a look at [this guide](https://support.smartbear.com/alertsite/docs/monitors/api/endpoint/jsonpath.html) or [this cheatsheet](https://lzone.de/#/LZone%20Cheat%20Sheets/Languages/JSONPath).

### Configuration Example

Here's an example of what the configuration in your entry point (`Program.cs`) would look like with the redacted fields configured:

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

> [!NOTE]
> 
> While the `RedactHeaders` config field accepts a list of case-insensitive headers, `RedactRequestBody` and `RedactResponseBody` expect a list of JSONPath strings as arguments. Also, the list of items to be redacted will be applied to all endpoint requests and responses on your server.

## Contributing and Help

To contribute to the development of this SDK or support requests, kindly read our [Contributors Guide](https://github.com/apitoolkit/.github/blob/main/CONTRIBUTING.md), join our community [Discord Server](https://discord.gg/dEB6EjQnKB), or [create a new issue](https://github.com/apitoolkit/apitoolkit-dotnet/issues/new/choose) in this repository.

## License

This repository is published under the [MIT](LICENSE) license.

---

<div align="center">
    
<a href="https://apitoolkit.iohttps://apitoolkit.io?utm_source=apitoolkit_github_dotnetsdk" target="_blank" rel="noopener noreferrer"><img src="https://github.com/apitoolkit/.github/blob/main/images/icon.png?raw=true" width="40" /></a>

</div>
