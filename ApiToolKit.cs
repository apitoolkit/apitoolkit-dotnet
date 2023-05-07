using System;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.PubSub.V1;
using Google.Protobuf.WellKnownTypes;
using Google.Protobuf;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiToolkit.Net
{
    public class APIToolkit
    {
        private readonly RequestDelegate _next;
        private readonly Client _client;
        Stopwatch stopwatch = new Stopwatch();

        public APIToolkit(RequestDelegate next, Client client)
        {
            _next = next;
            _client = client;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            context.Request.EnableBuffering(); // so we can read the body stream multiple times

            try 
            {
              await _next(context); // execute the next middleware in the pipeline
            } 
            finally 
            {
              var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
              context.Request.Body.Position = 0; // reset the body stream to the beginning

              // context.Response.Body.Seek(0, SeekOrigin.Begin);
              // var memoryStream = new MemoryStream();
              // await context.Response.Body.CopyToAsync(memoryStream);
              // context.Response.Body.Seek(0, SeekOrigin.Begin);

              var pathParams = context.GetRouteData().Values
                  .Where(v => !string.IsNullOrEmpty(v.Value?.ToString()))
                  .ToDictionary(v => v.Key, v => v.Value.ToString());

              var responseHeaders = context.Response.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
              var payload = _client.BuildPayload("DotNet", stopwatch, context.Request, context.Response.StatusCode,
                  System.Text.Encoding.UTF8.GetBytes(requestBody), await GetResponseBodyBytesAsync(context.Response.Body), responseHeaders,
                  pathParams, context.Request.Path);

              await _client.PublishMessageAsync(payload);
          }
        }

        public static async Task<byte[]> GetResponseBodyBytesAsync(Stream responseStream)
        {
            responseStream.Seek(0, SeekOrigin.Begin);
            var memoryStream = new MemoryStream();
            await responseStream.CopyToAsync(memoryStream);
            responseStream.Seek(0, SeekOrigin.Begin);

            return memoryStream.ToArray();
        }

        public static async Task<Client> NewClientAsync(Config cfg)
        {
            var url = "https://app.apitoolkit.io";
            if (!string.IsNullOrEmpty(cfg.RootUrl))
            {
                url = cfg.RootUrl;
            }
            
            var _httpClient = new HttpClient();
             _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {cfg.ApiKey}");
            var response = await _httpClient.GetAsync($"{url}/api/client_metadata");
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Unable to query apitoolkit for client metadata: {response.StatusCode}");
            }

            var clientMetadata = JsonConvert.DeserializeObject<ClientMetadata>(await response.Content.ReadAsStringAsync());
            if (clientMetadata is null)
            {
                throw new Exception("Unable to deserialize client metadata response");
            }

            var credentials = GoogleCredential
                .FromJson(clientMetadata.PubsubPushServiceAccount.ToString())
                .CreateScoped(PublisherServiceApiClient.DefaultScopes);

            var publisher = new PublisherClientBuilder();
            publisher.Credential = credentials;
            publisher.TopicName = new TopicName(clientMetadata.PubsubProjectId, clientMetadata.TopicId);
            var pubsubClient = publisher.Build();
            var client = new Client(pubsubClient, null, cfg, clientMetadata);
            if (client.Config.Debug)
            {
                Console.WriteLine("APIToolkit: client initialized successfully");
            }
            return client;
        }
    }


    public class Client
    {
        public readonly PublisherClient PubSubClient;
        public readonly TopicName TopicName;
        public  readonly Config Config;
        public  readonly ClientMetadata Metadata;

        public Client(PublisherClient pubSubClient, TopicName topicName, Config config, ClientMetadata metadata)
        {
            PubSubClient = pubSubClient;
            TopicName = topicName;
            Config = config;
            Metadata = metadata;
        }

        public async Task PublishMessageAsync(Payload payload)
        {
            if (PubSubClient == null)
            {
                if (Config.Debug)
                {
                    Console.WriteLine("APIToolkit: topic is not initialized. Check client initialization. Messages are not being sent to apitoolkit");
                }
                return;
            }

            await PubSubClient.PublishAsync(new PubsubMessage {
                Data = ByteString.CopyFromUtf8(JsonConvert.SerializeObject(payload)),
                PublishTime = Timestamp.FromDateTime(DateTime.UtcNow),
            });

            if (Config.Debug)
            {
                Console.WriteLine("APIToolkit: message published to pubsub topic");

                if (Config.VerboseDebug)
                {
                  Console.WriteLine($"APIToolkit: {JsonConvert.SerializeObject(payload)}");
                }
            }
        }

        public Payload BuildPayload(string SDKType, Stopwatch stopwatch, HttpRequest req, int statusCode, byte[] reqBody, byte[] respBody, Dictionary<string, List<string>> respHeader, Dictionary<string, string> pathParams, string urlPath)
        {
            if (req == null)
            {
                // Early return with empty payload to prevent any null reference exceptions
                if (Config.Debug)
                {
                    Console.WriteLine("APIToolkit: null request or client or url while building payload.");
                }
                return new Payload();
            }
            string projectId = Metadata is null ? "" : Metadata.ProjectId;

            var reqHeaders = req.Headers.ToDictionary(h => h.Key,h => h.Value.ToList());
            int[] versionParts = req.Protocol.Split('/', '.').Skip(1).Select(int.Parse).ToArray();
            var (majorVersion, minorVersion) = versionParts.Length >= 2 ? (versionParts[0], versionParts[1]) : (1, 1);

            stopwatch.Stop();
            return new Payload
            {
                Duration = stopwatch.ElapsedTicks * 100,
                Host = req.Host.Host,
                Method = req.Method,
                PathParams = pathParams, 
                ProjectId = projectId,
                ProtoMajor = majorVersion,
                ProtoMinor = minorVersion,
                QueryParams = req.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
                RawUrl = req.GetEncodedPathAndQuery(),
                Referer = req.Headers["Referer"].ToString(),
                RequestBody = RedactJSON(reqBody, Config.RedactRequestBody),
                RequestHeaders = RedactHeaders(reqHeaders, Config.RedactHeaders),
                ResponseBody = RedactJSON(respBody, Config.RedactResponseBody),
                ResponseHeaders = RedactHeaders(respHeader, Config.RedactHeaders),
                SdkType = SDKType,
                StatusCode = statusCode,
                Timestamp = DateTime.UtcNow,
                UrlPath = urlPath,
            };
        }


        public static byte[] RedactJSON(byte[] data, List<string> jsonPaths)
        {
            JObject jsonObject = JObject.Parse(System.Text.Encoding.UTF8.GetString(data));
            (jsonPaths ?? new List<string>()).ForEach(jPath => jsonObject.SelectTokens(jPath).ToList().ForEach(token => token.Replace("[CLIENT_REDACTED]")));
            return System.Text.Encoding.UTF8.GetBytes(jsonObject.ToString());
        }


        public static Dictionary<string, List<string>> RedactHeaders(Dictionary<string, List<string>> headers, List<string> redactList)
        {
            redactList = (redactList ?? new List<string>()).Select(s => s.ToLower()).ToList();
            return headers
                .ToDictionary(
                    kvp => redactList.Contains(kvp.Key.ToLower()) ? kvp.Key : kvp.Key,
                    kvp => redactList.Contains(kvp.Key.ToLower()) ? new List<string> { "[CLIENT_REDACTED]" } : kvp.Value
                );
        }
    }


     public class ClientMetadata
    {
        [JsonProperty("project_id")]
        public string ProjectId { get; set; }

        [JsonProperty("pubsub_project_id")]
        public string PubsubProjectId { get; set; }

        [JsonProperty("topic_id")]
        public string TopicId { get; set; }

        [JsonProperty("pubsub_push_service_account")]
        public JRaw PubsubPushServiceAccount { get; set; }
    }

    public class Config
    {
        [JsonProperty("debug")]
        public bool Debug { get; set; }

        [JsonProperty("verbose_debug")]
        public bool VerboseDebug { get; set; }

        [JsonProperty("root_url")]
        public string RootUrl { get; set; }

        [JsonProperty("api_key")]
        public string ApiKey { get; set; }

        [JsonProperty("project_id")]
        public string ProjectId { get; set; }

        [JsonProperty("redact_headers")]
        public List<string> RedactHeaders { get; set; }

        [JsonProperty("redact_request_body")]
        public List<string> RedactRequestBody { get; set; }
        
        [JsonProperty("redact_response_body")]
        public List<string> RedactResponseBody { get; set; }
    }

    public class Payload
    {
        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("request_headers")]
        public Dictionary<string, List<string>> RequestHeaders { get; set; }

        [JsonProperty("query_params")]
        public Dictionary<string, List<string>> QueryParams { get; set; }

        [JsonProperty("path_params")]
        public Dictionary<string, string> PathParams { get; set; }

        [JsonProperty("response_headers")]
        public Dictionary<string, List<string>> ResponseHeaders { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("sdk_type")]
        public string SdkType { get; set; }

        [JsonProperty("host")]
        public string Host { get; set; }

        [JsonProperty("raw_url")]
        public string RawUrl { get; set; }

        [JsonProperty("referer")]
        public string Referer { get; set; }

        [JsonProperty("project_id")]
        public string ProjectId { get; set; }

        [JsonProperty("url_path")]
        public string UrlPath { get; set; }

        [JsonProperty("response_body")]
        public byte[] ResponseBody { get; set; }

        [JsonProperty("request_body")]
        public byte[] RequestBody { get; set; }

        [JsonProperty("proto_minor")]
        public int ProtoMinor { get; set; }

        [JsonProperty("status_code")]
        public int StatusCode { get; set; }

        [JsonProperty("proto_major")]
        public int ProtoMajor { get; set; }
        
        //Nanoseconds
        [JsonProperty("duration")]
        public long Duration { get; set; }
    }
}
