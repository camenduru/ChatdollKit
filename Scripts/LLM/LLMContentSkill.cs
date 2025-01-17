﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.Dialog.Processor;
using ChatdollKit.Model;

namespace ChatdollKit.LLM
{
    public class LLMContentSkill : SkillBase
    {
        [Header("Response stream settings")]
        public List<string> SplitChars = new List<string>() { "。", "！", "？", ".", "!", "?" };
        private List<string> splitCharsWithNewLine;
        public List<string> OptionalSplitChars = new List<string>() { "、", "," };
        public int MaxLengthBeforeOptionalSplit = 0;

        public Action<string, AnimatedVoiceRequest> HandleSplittedText;

        protected ILLMService llmService { get; set; }
        protected List<AnimatedVoiceRequest> responseAnimations { get; set; } = new List<AnimatedVoiceRequest>();
        protected Dictionary<string, Model.Animation> animationsToPerform { get; set; } = new Dictionary<string, Model.Animation>();
        public bool IsParsing { get; protected set; } = false;

        public void SetLLMService(ILLMService llmService)
        {
            this.llmService = llmService;
        }

        public void RegisterAnimation(string name, Model.Animation animation)
        {
            animationsToPerform.Add(name, animation);
        }

#pragma warning disable CS1998
        public override async UniTask<Response> ProcessAsync(Request request, State state, User user, CancellationToken token)
        {
            // Just set LLMSession to response
            var llmSession = (ILLMSession)request.Payloads["LLMSession"];
            var response = new Response(request.Id, endTopic: false);
            response.Payloads = new Dictionary<string, object>()
            {
                { "LLMSession", llmSession }
            };

            return response;
        }
#pragma warning restore CS1998

        public override async UniTask ShowResponseAsync(Response response, Request request, State state, CancellationToken token)
        {
            // NOTE: USE LLMSession in response.Payloads because that may be updated at ProcessAsync (e.g. function calling)

            // Parse payloads
            var payloads = (Dictionary<string, object>)response.Payloads;
            var llmSession = (ILLMSession)payloads["LLMSession"];

            // Clear responseAnimations before parsing and performing
            responseAnimations.Clear();

            // Start parsing voices, faces and animations and performing them concurrently
            var parseTask = ParseAnimatedVoiceAsync(llmSession, token);
            var performTask = PerformAnimatedVoiceAsync(llmSession, token);

            // Wait API stream ends
            await llmSession.StreamingTask;
            await llmSession.OnStreamingEnd();

            // Wait parsing and performance
            await parseTask;
            await performTask;
        }

        protected virtual async UniTask ParseAnimatedVoiceAsync(ILLMSession llmSession, CancellationToken token)
        {
            IsParsing = true;

            // Split current buffer with the marks that represents the end of a sentence
            splitCharsWithNewLine = new List<string>(SplitChars) { "\n" };

            try
            {
                var facePattern = @"\[face:(.+?)\]";
                var animPattern = @"\[anim:(.+?)\]";
                var splitIndex = 0;
                var isFirstAnimatedVoice = true;

                while (!token.IsCancellationRequested)
                {
                    // Split current buffer with the marks that represents the end of a sentence
                    var splittedBuffer = SplitString(llmSession.StreamBuffer);

                    if (llmSession.IsResponseDone && splitIndex == splittedBuffer.Count)
                    {
                        // Exit while loop when stream response ends and all sentences has been processed
                        break;
                    }

                    if (splittedBuffer.Count() > splitIndex + 1 || llmSession.IsResponseDone)
                    {
                        // Process each splitted unprocessed sentence
                        foreach (var text in splittedBuffer.Skip(splitIndex).Take(llmSession.IsResponseDone ? splittedBuffer.Count - splitIndex : 1))
                        {
                            splitIndex += 1;
                            if (!string.IsNullOrEmpty(text.Trim()))
                            {
                                var avreq = new AnimatedVoiceRequest(startIdlingOnEnd: isFirstAnimatedVoice);
                                isFirstAnimatedVoice = false;
                                var textToSay = text;
                                var ttsConfig = new TTSConfiguration();

                                // Parse face tags and remove it from text to say
                                var faceMatches = Regex.Matches(textToSay, facePattern);
                                textToSay = Regex.Replace(textToSay, facePattern, "");

                                // Parse animation tags and remove it from text to say
                                var animMatches = Regex.Matches(textToSay, animPattern);
                                textToSay = Regex.Replace(textToSay, animPattern, "");

                                // Remove other tags (sometimes invalid format like `[smile]` remains)
                                textToSay = Regex.Replace(textToSay, @"\[(.+?)\]", "");

                                // Add voice
                                avreq.AddVoiceTTS(textToSay, postGap: textToSay.EndsWith("。") ? 0 : 0.3f);

                                var logMessage = textToSay;

                                if (faceMatches.Count > 0)
                                {
                                    // Add face if face tag included
                                    var face = faceMatches[0].Groups[1].Value;
                                    avreq.AddFace(face, duration: 7.0f);
                                    logMessage = $"[face:{face}]" + logMessage;
                                    // Set face as style parameter to voice
                                    ttsConfig.Params["style"] = face;                                   
                                    avreq.AnimatedVoices.Last().Voices.Last().TTSConfig = ttsConfig;
                                }

                                if (animMatches.Count > 0)
                                {
                                    // Add animation if anim tag included
                                    var anim = animMatches[0].Groups[1].Value;
                                    if (animationsToPerform.ContainsKey(anim))
                                    {
                                        var a = animationsToPerform[anim];
                                        avreq.AddAnimation(a.ParameterKey, a.ParameterValue, a.Duration, a.LayeredAnimationName, a.LayeredAnimationLayerName);
                                        logMessage = $"[anim:{anim}]" + logMessage;
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"Animation {anim} is not registered.");
                                    }
                                }

                                HandleSplittedText?.Invoke(text, avreq);

                                Debug.Log($"Assistant: {logMessage}");

                                // Set AnimatedVoiceRequest to queue
                                responseAnimations.Add(avreq);

                                // Prefetch the voice from TTS service
                                _ = modelController.TextToSpeechFunc.Invoke(new Voice(string.Empty, 0.0f, 0.0f, textToSay, string.Empty, ttsConfig, VoiceSource.TTS, true, string.Empty), token);
                            }
                        }
                    }

                    // Wait for a bit before processing buffer next time
                    await UniTask.Delay(100, cancellationToken: token);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"ParseAnimatedVoiceAsync was cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at ParseAnimatedVoiceAsync: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                IsParsing = false;
            }
        }

        private List<string> SplitString(string input)
        {
            var result = new List<string>();
            var tempBuffer = "";

            for (int i = 0; i < input.Length; i++)
            {
                tempBuffer += input[i];

                if (IsSplitChar(input[i].ToString()))
                {
                    result.Add(tempBuffer);
                    tempBuffer = "";
                }
                else if (IsOptionalSplitChar(input[i].ToString()))
                {
                    if (tempBuffer.Length >= MaxLengthBeforeOptionalSplit)
                    {
                        result.Add(tempBuffer);
                        tempBuffer = "";
                    }
                }
            }

            if (!string.IsNullOrEmpty(tempBuffer))
            {
                result.Add(tempBuffer);
            }

            return result;
        }

        private bool IsSplitChar(string character)
        {
            foreach (var splitChar in splitCharsWithNewLine)
            {
                if (character == splitChar)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsOptionalSplitChar(string character)
        {
            foreach (var splitChar in OptionalSplitChars)
            {
                if (character == splitChar)
                {
                    return true;
                }
            }
            return false;
        }

        protected virtual async UniTask PerformAnimatedVoiceAsync(ILLMSession llmSession, CancellationToken token)
        {
            var isFirstVoice = true;
            while (!token.IsCancellationRequested)
            {
                // Performance ends when streaming response and parsing ends and all response animated voices are done
                if (llmSession.IsResponseDone && !IsParsing && responseAnimations.Count == 0) break;

                if (responseAnimations.Count > 0)
                {
                    // Retrive AnimatedVoice from queue
                    var req = responseAnimations[0];
                    responseAnimations.RemoveAt(0);

                    if (isFirstVoice)
                    {
                        if (req.AnimatedVoices[0].Faces.Count == 0)
                        {
                            // Reset face expression at the beginning of animated voice
                            req.AddFace("Neutral");
                        }
                        isFirstVoice = false;
                    }

                    // Perform
                    await modelController.AnimatedSay(req, token);
                }
                else
                {
                    // Do nothing (just wait a bit) when no AnimatedVoice in the queue while receiving data
                    await UniTask.Delay(100, cancellationToken: token);
                }
            }
        }
    }
}
