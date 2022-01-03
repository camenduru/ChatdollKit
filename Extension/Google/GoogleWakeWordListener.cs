﻿using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog;
using ChatdollKit.IO;

namespace ChatdollKit.Extension.Google
{
    public class GoogleWakeWordListener : WakeWordListenerBase
    {
        [Header("Google Cloud Settings")]
        public string ApiKey = string.Empty;
        public string Language = "ja-JP";
        public bool UseEnhancedModel = false;

        public void Configure(string apiKey, string language, bool overwrite = false)
        {
            ApiKey = string.IsNullOrEmpty(ApiKey) || overwrite ? apiKey : ApiKey;
            Language = string.IsNullOrEmpty(Language) || overwrite ? language : Language;
        }

        protected override async UniTask<string> RecognizeSpeechAsync(AudioClip recordedVoice)
        {
            if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(Language))
            {
                Debug.LogError("API Key or Language are missing from GoogleWakeWordListener");
            }

            var response = await client.PostJsonAsync<SpeechRecognitionResponse>(
                $"https://speech.googleapis.com/v1/speech:recognize?key={ApiKey}",
                new SpeechRecognitionRequest(recordedVoice, Language, UseEnhancedModel));

            return response?.results?[0]?.alternatives?[0]?.transcript ?? string.Empty;
        }

#pragma warning disable CS0649
        // Models for request and response
        class SpeechRecognitionRequest
        {
            public SpeechRecognitionConfig config;
            public SpeechRecognitionAudio audio;

            public SpeechRecognitionRequest(AudioClip audioClip, string languageCode, bool useEnhancedModel)
            {
                config = new SpeechRecognitionConfig(audioClip, languageCode, useEnhancedModel);
                audio = new SpeechRecognitionAudio(AudioConverter.AudioClipToBase64(audioClip));
            }
        }

        class SpeechRecognitionConfig
        {
            public int encoding;
            public double sampleRateHertz;
            public double audioChannelCount;
            public bool enableSeparateRecognitionPerChannel;
            public string languageCode;
            public string model;
            public bool useEnhanced;

            public SpeechRecognitionConfig(AudioClip audioClip, string languageCode, bool useEnhancedModel)
            {
                encoding = 1;   // 1: 16-bit linear PCM
                sampleRateHertz = audioClip.frequency;
                audioChannelCount = audioClip.channels;
                enableSeparateRecognitionPerChannel = false;
                this.languageCode = languageCode;
                model = useEnhancedModel ? null : "default";
                useEnhanced = useEnhancedModel;
            }
        }

        class SpeechRecognitionAudio
        {
            public string content;

            public SpeechRecognitionAudio(string content)
            {
                this.content = content;
            }
        }

        class SpeechRecognitionResponse
        {
            public SpeechRecognitionResult[] results;
        }

        class SpeechRecognitionResult
        {
            public SpeechRecognitionAlternative[] alternatives;
        }

        class SpeechRecognitionAlternative
        {
            public string transcript;
            public double confidence;
        }
    }
}
