// Copyright (c) 2021 David Bevin
// 
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bevo.ReverseProxy.Kube
{
    public class RequestLogMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger _logger;

        public RequestLogMiddleware(RequestDelegate next, ILogger<RequestLogMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var requestTime = DateTime.Now;
            var stopwatch = Stopwatch.StartNew();

            await _next(context);

            stopwatch.Stop();

            // 0.0.0.1             - - [01/Mar/2021:20:58:10 +11:00]  "GET /hello HTTP/1.1" 404     0                ""              "PostmanRuntime/7.26.10" ???             0.001
            // 192.168.65.3        - - [01/Mar/2021:07:14:42 +0000]   "GET /hello HTTP/1.1" 200     2077             "-"             "PostmanRuntime/7.26.10" 238             0.004         [dev-echo-echo-server-http] []                               10.1.0.71:80   2077                      0.010                   200              596ec0040facc91deafc404f4899cc94
            // '$remote_addr - $remote_user [$time_local]             "$request"            $status $body_bytes_sent "$http_referer" "$http_user_agent"       $request_length $request_time $proxy_upstream_name        $proxy_alternative_upstream_name $upstream_addr $upstream_response_length $upstream_response_time $upstream_status $req_id'
            // See https://github.com/kubernetes/ingress-nginx/blob/master/docs/user-guide/nginx-configuration/log-format.md
            var remoteAddress = context.Request.HttpContext.Connection.RemoteIpAddress;
            var request = $"{context.Request.Method.ToUpper()} {context.Request.Path} {context.Request.Protocol}";
            var status = context.Response.StatusCode;
            var bodyBytesSent = 0;  // We can't get this because we're streaming
            var httpReferer = context.Request.Headers["Referer"];
            var httpUserAgent = context.Request.Headers["User-Agent"];
            _logger.LogInformation($"{remoteAddress} - - [{requestTime:dd/MMM/yyyy:HH:mm:ss zzz}] \"{request}\" {context.Response.StatusCode} {bodyBytesSent} \"{httpReferer}\" \"{httpUserAgent}\" ??? {stopwatch.Elapsed.TotalSeconds:F3}");
        }
    }
}
