using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Collections.Generic;
using ApiToolkit.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Extensions.Primitives;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using System.Diagnostics;

public class RedactTests
{
    [Test]
    public void TestRedact_Success()
    {
        // Arrange
        string jsonString = @"
            {
                'name': 'John Doe',
                'addresses': [{
                    'street': '123 Main St',
                    'city': {'name':'Anytown'},
                    'state': 'CA',
                    'house_no': {'number':'5A', 'floor': 'floor'},
                    'zip': ['12345', '0987']
                }],
                'phone': '555-555-1212',
                'email': 'jdoe@example.com'
            }";
        List<string> jsonPaths = new List<string>
        {
            "$.name",
            "$.addresses[*].street",
            "$.addresses[*].city.name",
            "$.phone",
            "$.addresses[*].house_no",
            "$.addresses[*].zip[*]",
            "$.invalid.path" // NotFound paths should not affect final json
        };

        // Act
        byte[] result = Client.RedactJSON(Encoding.UTF8.GetBytes(jsonString), jsonPaths);

        // Assert
        JObject expectedObject = JObject.Parse(@"
            {
                'name': '[CLIENT_REDACTED]',
                'addresses': [{
                    'street': '[CLIENT_REDACTED]',
                    'city': {'name': '[CLIENT_REDACTED]'},
                    'state': 'CA',
                    'house_no': '[CLIENT_REDACTED]',
                    'zip': ['[CLIENT_REDACTED]', '[CLIENT_REDACTED]']
                }],
                'phone': '[CLIENT_REDACTED]',
                'email': 'jdoe@example.com'
            }");

        JObject resultObject = JObject.Parse(Encoding.UTF8.GetString(result));
        Assert.IsTrue(JToken.DeepEquals(expectedObject, resultObject));
    }

    [Test]
    public void TestRedactHeaders()
    {
        // Define test data
        var headers = new Dictionary<string, List<string>>
        {
            { "Content-Type", new List<string> { "application/json" } },
            { "Authorization", new List<string> { "Bearer abcdefg" } },
            { "X-Forwarded-For", new List<string> { "127.0.0.1" } }
        };
        var redactList = new List<string> { "authorization", "x-forwarded-for" , "invalid-one"};

        // Test success case
        var expected = new Dictionary<string, List<string>>
        {
            { "Content-Type", new List<string> { "application/json" } },
            { "Authorization", new List<string> { "[CLIENT_REDACTED]" } },
            { "X-Forwarded-For", new List<string> { "[CLIENT_REDACTED]" } }
        };
        var actual = Client.RedactHeaders(headers, redactList);
        Assert.That(actual, Is.EqualTo(expected));

        // Test failure case
        redactList = null;
        expected = headers;
        actual = Client.RedactHeaders(headers, redactList);
        Assert.That(actual, Is.EqualTo(expected));
    }

  [Test]
  public void TestBuildPayload()
  {
      // Arrange
      var sdkType = "test-sdk";
      // Create a new HttpContext with custom request headers and query parameters
      var req = new DefaultHttpContext().Request;
      req.Method = "POST";
      req.Host = new HostString("example.com");
      req.Path = "/path/to/resource";
      req.Headers.Add("Authorization", "Value");
      req.Query = new QueryCollection(new Dictionary<string, StringValues>
      {
          { "param1", "value1" },
          { "param2", "value2" }
      });

      var statusCode = 200;
      var reqBody = Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}");
      var respBody = Encoding.UTF8.GetBytes("{\"baz\":42}");
      var respHeaders = new Dictionary<string, List<string>>
      {
          { "Content-Type", new List<string> { "application/json" } }
      };
      var pathParams = new Dictionary<string, string>
      {
          { "id", "123" }
      };
      var urlPath = "/path/to/resource/{id}";
      var config = new Config
      {
          RedactHeaders = new List<string> { "Authorization" },
          RedactRequestBody = new List<string> { "$.foo" },
          Debug = true
      };

      Stopwatch stopwatch = new Stopwatch();
      stopwatch.Start();

      var client = new Client(null, null, config, null);

      // Act
      var payload = client.BuildPayload(sdkType, stopwatch, req, statusCode, reqBody, respBody, respHeaders, pathParams, urlPath);

      // Assert
      Assert.AreEqual(req.Host.Host, payload.Host);
      Assert.AreEqual(req.Method, payload.Method);
      Assert.AreEqual(new Dictionary<string, string>
      {
          { "id", "123" },
      }, payload.PathParams);
      Assert.AreEqual(1, payload.ProtoMajor);
      Assert.AreEqual(1, payload.ProtoMinor);
      Assert.AreEqual(new Dictionary<string, List<string>>
      {
          { "param1", new List<string> { "value1" } },
          { "param2", new List<string> { "value2" } }
      }, payload.QueryParams);
      Assert.AreEqual(req.Path + req.QueryString, payload.RawUrl);
      Assert.AreEqual(req.Headers["Referer"].ToString(), payload.Referer);
      Assert.AreEqual("{\n  \"foo\": \"[CLIENT_REDACTED]\"\n}", Encoding.UTF8.GetString(payload.RequestBody));
      Assert.AreEqual("[CLIENT_REDACTED]", payload.RequestHeaders["Authorization"][0]);
      Assert.AreEqual("{\n  \"baz\": 42\n}", Encoding.UTF8.GetString(payload.ResponseBody));
      Assert.AreEqual("application/json", payload.ResponseHeaders["Content-Type"][0]);
      Assert.AreEqual(sdkType, payload.SdkType);
      Assert.AreEqual(statusCode, payload.StatusCode);
      Assert.AreEqual("POST", payload.Method);
      Assert.GreaterOrEqual(DateTime.UtcNow, payload.Timestamp);
      Assert.AreEqual(urlPath, payload.UrlPath);
      Console.WriteLine($"APIToolkit: {JsonConvert.SerializeObject(payload)}");
  }


  [Test]
  public async Task GetResponseBodyBytesAsync_ReturnsCorrectBytes()
  {
      // Arrange
      var context = new DefaultHttpContext();
      var responseBody = "This is the response body";
      var responseBytes = Encoding.UTF8.GetBytes(responseBody);
      context.Response.Body = new MemoryStream(responseBytes);
      
      // Act
      var bytes = await APIToolkit.GetResponseBodyBytesAsync(context.Response.Body);

      // Assert
      Assert.AreEqual(responseBytes, bytes);
  }
}
