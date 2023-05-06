using System;
using System.Diagnostics;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ApiToolKit.Net
{
    public class APIToolKit
    {
        private readonly RequestDelegate _next;
        private readonly Client _client;
        Stopwatch stopwatch = new Stopwatch();

        public APIToolKit(RequestDelegate next, Client client)
        {
            _next = next;
            _client = client;
        }

        // public async Task InvokeAsync(HttpContext context, Client client)
        // {
        //     try
        //     {
        //         stopwatch.Start();

                

        //         stopwatch.Stop();

        //         await _next(context);
        //     }
        //     catch (System.Exception)
        //     {
                
        //         throw;
        //     }
        // }

        public async Task InvokeAsync(HttpContext context)
        {
            var start = DateTime.UtcNow;
            var request = context.Request;
            request.EnableBuffering(); // so we can read the body stream multiple times

            var requestBody = await new StreamReader(request.Body).ReadToEndAsync();
            request.Body.Position = 0; // reset the body stream to the beginning

            var responseBodyStream = new MemoryStream();
            var originalResponseBodyStream = context.Response.Body;
            context.Response.Body = responseBodyStream;

            await _next(context); // execute the next middleware in the pipeline

            responseBodyStream.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(responseBodyStream).ReadToEndAsync();
            responseBodyStream.Seek(0, SeekOrigin.Begin);

            var pathParams = new Dictionary<string, string>();
            // foreach (var param in context.Request.RouteValues)
            // {
            //     pathParams[param.Key] = param.Value.ToString();
            // }


            var payload = _client.BuildPayload("DotNet", start, context.Request, context.Response.StatusCode,
                System.Text.Encoding.UTF8.GetBytes(requestBody), System.Text.Encoding.UTF8.GetBytes(responseBody), context.Response.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList()),
                pathParams, context.Request.Path);

            await _client.PublishMessageAsync(payload);

            // restore the original response body stream
            await responseBodyStream.CopyToAsync(originalResponseBodyStream);
            context.Response.Body = originalResponseBodyStream;
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
            // if (goReqsTopic == null)
            // {
            //     if (config.Debug)
            //     {
            //         Console.WriteLine("APIToolkit: topic is not initialized. Check client initialization");
            //     }
            //     throw new Exception("topic is not initialized");
            // }

            var jsonPayload = JsonConvert.SerializeObject(payload);

            Console.WriteLine($"PublishMessageAsync JsonPayLoad ----- {jsonPayload}");

            var message = new PubsubMessage
            {
                Data = ByteString.CopyFromUtf8(jsonPayload),
                PublishTime = Timestamp.FromDateTime(DateTime.UtcNow),
            };

            await PubSubClient.PublishAsync(message);

            // if (config.Debug)
            // {
            //     Console.WriteLine("APIToolkit: message published to pubsub topic");

            //     if (config.VerboseDebug)
            //     {
            //         Console.WriteLine($"APIToolkit: {jsonPayload}");
            //     }
            // }
        }

        public Payload BuildPayload(string SDKType, DateTime trackingStart, HttpRequest req, int statusCode, byte[] reqBody, byte[] respBody, Dictionary<string, List<string>> respHeader, IDictionary<string, string> pathParams, string urlPath)
        {
            if (req == null)
            {
                // Early return with empty payload to prevent any null reference exceptions
                // if (config.Debug)
                // {
                //     Console.WriteLine("APIToolkit: null request or client or url while building payload.");
                // }
                return new Payload();
            }
            string projectId = "";
            if (Metadata != null)
            {
                projectId = Metadata.ProjectId;
            }

            // List<string> redactedHeaders = new List<string>();
            // foreach (var v in Config.RedactHeaders)
            // {
            //     redactedHeaders.Add(v.ToLower());
            // }

            var reqHeaders = req.Headers.ToDictionary(h => h.Key,h => h.Value.ToList());

            TimeSpan since = DateTime.UtcNow.Subtract(trackingStart);
            return new Payload
            {
                Duration = since.Ticks / 100,
                Host = req.Host.Host,
                Method = req.Method,
                PathParams = null, // replace with appropriate code if necessary
                ProjectId = projectId,
                ProtoMajor = 1,
                ProtoMinor = 1,
                // QueryParams = req.RequestUri.Query.ToDictionary(h => h.Key,h => h.Value.ToList()),
                QueryParams = null,
                // RawUrl = req.RequestUri.AbsoluteUri,
                // Referer = req.Headers.Referrer?.AbsoluteUri,
                RawUrl = req.Scheme + "://" + req.Host + req.Path + req.QueryString,
                Referer = req.Headers["Referer"].ToString(),
                RequestBody = Redact(reqBody, Config.RedactRequestBody),
                RequestHeaders = RedactHeaders(reqHeaders, Config.RedactHeaders),
                ResponseBody = Redact(respBody, Config.RedactResponseBody),
                ResponseHeaders = RedactHeaders(respHeader, Config.RedactHeaders),
                SdkType = SDKType,
                StatusCode = statusCode,
                Timestamp = DateTime.UtcNow,
                UrlPath = urlPath,
            };
        }

        public static byte[] Redact(byte[] data, List<string> redactList)
        {
            // var obj = JToken.Parse(System.Text.Encoding.UTF8.GetString(data));
            // var config = new JsonPath.JsonPathConfig { UseXmlNotation = true };
            // var path = new JsonPath.JsonPath(config);
            // foreach (var key in redactList)
            // {
            //     var results = path.Evaluate(key, obj);
            //     foreach (var result in results)
            //     {
            //         if (result.Value is JsonPath.Accessor accessor)
            //         {
            //             accessor.Value = "[CLIENT_REDACTED]";
            //         }
            //     }
            // }
            // return System.Text.Encoding.UTF8.GetBytes(obj.ToString());
            return data;
        }

        public static Dictionary<string, List<string>> RedactHeaders(Dictionary<string, List<string>> headers, List<string> redactList)
        {
            if(redactList is null)
            {
                return headers;
            }
            foreach (var key in headers.Keys.ToArray())
            {
                if (redactList.Contains(key.ToLower()))
                {
                    headers[key] = new string[] { "[CLIENT_REDACTED]" }.ToList();
                }
            }
            return headers;
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
