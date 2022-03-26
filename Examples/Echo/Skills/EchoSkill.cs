﻿using System.Threading;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;

namespace ChatdollKit.Examples.Echo
{
    public class EchoSkill : SkillBase
    {
#pragma warning disable CS1998
        public override async UniTask<Response> ProcessAsync(Request request, State state, CancellationToken token)
        {
            // Build and return response message
            var response = new Response(request.Id);

            // Set what user said to response
            response.AddVoiceTTS($"{request.Text}");

            return response;
        }
#pragma warning restore CS1998
    }
}
