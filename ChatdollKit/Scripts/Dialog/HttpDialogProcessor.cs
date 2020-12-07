﻿using System;
using System.Threading;
using System.Threading.Tasks;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class HttpDialogProcessor : DialogProcessorBase
    {
        public string DialogUri;
        protected ChatdollHttp httpClient = new ChatdollHttp();

        private void OnDestroy()
        {
            httpClient?.Dispose();
        }

        public override async Task<Response> PreProcessAsync(Request request, Context context, CancellationToken token)
        {
            var httpDialogResponse = await httpClient.PostJsonAsync<HttpDialogResponse>(DialogUri, new HttpDialogRequest(request, context, true));

            if (httpDialogResponse.Context != null)
            {
                // Update status and data
                context.Topic.Status = httpDialogResponse.Context.Topic.Status;
                context.Data = httpDialogResponse.Context.Data;
            }

            return httpDialogResponse.Response;
        }

        // Process dialog on server
        public override async Task<Response> ProcessAsync(Request request, Context context, CancellationToken token)
        {
            var httpDialogResponse = await httpClient.PostJsonAsync<HttpDialogResponse>(DialogUri, new HttpDialogRequest(request, context));

            // Update topic
            context.Topic.Status = httpDialogResponse.Context.Topic.Status;
            context.Topic.ContinueTopic = httpDialogResponse.Context.Topic.ContinueTopic;
            context.Topic.RequiredRequestType = httpDialogResponse.Context.Topic.RequiredRequestType;

            // Update data
            context.Data = httpDialogResponse.Context.Data;

            // Update user info
            request.User.Name = httpDialogResponse.Request.User.Name;
            request.User.Nickname = httpDialogResponse.Request.User.Nickname;
            request.User.Data = httpDialogResponse.Request.User.Data;

            return httpDialogResponse.Response;
        }

        // Request message
        private class HttpDialogRequest
        {
            public Request Request { get; set; }
            public Context Context { get; set; }
            public bool PreProcess { get; set; }

            public HttpDialogRequest(Request request, Context context, bool preProcess = false)
            {
                Request = request;
                Context = context;
                PreProcess = preProcess;
            }
        }

        // Response message
        private class HttpDialogResponse
        {
            public Request Request { get; set; }
            public Context Context { get; set; }
            public Response Response { get; set; }
            public HttpDialogError Error { get; set; }
        }

        // Error info in response
        private class HttpDialogError
        {
            public string Code { get; set; }
            public string Message { get; set; }
            public Exception Exception { get; set; }
        }
    }
}
