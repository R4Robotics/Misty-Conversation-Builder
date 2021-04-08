﻿/**********************************************************************
	Copyright 2021 Misty Robotics
	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at
		http://www.apache.org/licenses/LICENSE-2.0
	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
	**WARRANTY DISCLAIMER.**
	* General. TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW, MISTY
	ROBOTICS PROVIDES THIS SAMPLE SOFTWARE "AS-IS" AND DISCLAIMS ALL
	WARRANTIES AND CONDITIONS, WHETHER EXPRESS, IMPLIED, OR STATUTORY,
	INCLUDING THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
	PURPOSE, TITLE, QUIET ENJOYMENT, ACCURACY, AND NON-INFRINGEMENT OF
	THIRD-PARTY RIGHTS. MISTY ROBOTICS DOES NOT GUARANTEE ANY SPECIFIC
	RESULTS FROM THE USE OF THIS SAMPLE SOFTWARE. MISTY ROBOTICS MAKES NO
	WARRANTY THAT THIS SAMPLE SOFTWARE WILL BE UNINTERRUPTED, FREE OF VIRUSES
	OR OTHER HARMFUL CODE, TIMELY, SECURE, OR ERROR-FREE.
	* Use at Your Own Risk. YOU USE THIS SAMPLE SOFTWARE AND THE PRODUCT AT
	YOUR OWN DISCRETION AND RISK. YOU WILL BE SOLELY RESPONSIBLE FOR (AND MISTY
	ROBOTICS DISCLAIMS) ANY AND ALL LOSS, LIABILITY, OR DAMAGES, INCLUDING TO
	ANY HOME, PERSONAL ITEMS, PRODUCT, OTHER PERIPHERALS CONNECTED TO THE PRODUCT,
	COMPUTER, AND MOBILE DEVICE, RESULTING FROM YOUR USE OF THIS SAMPLE SOFTWARE
	OR PRODUCT.
	Please refer to the Misty Robotics End User License Agreement for further
	information and full details:
		https://www.mistyrobotics.com/legal/end-user-license-agreement/
**********************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Conversation.Common;
using MistyRobotics.Common.Types;
using MistyRobotics.SDK.Events;
using MistyRobotics.SDK.Messengers;
using MistyRobotics.SDK.Responses;
using SkillTools.AssetTools;
using SpeechTools;
using SpeechTools.AzureCognitive;
using SpeechTools.GoogleSpeech;

namespace MistyCharacter
{
	public class SpeechManager : BaseManager, ISpeechManager
	{
		public event EventHandler<string> StartedSpeaking;
		public event EventHandler<IAudioPlayCompleteEvent> StoppedSpeaking;
		public event EventHandler<DateTime> StartedListening;
		public event EventHandler<IVoiceRecordEvent> StoppedListening;
		public event EventHandler<TriggerData> SpeechIntent;		
		public event EventHandler<bool> KeyPhraseRecognitionOn;
		public event EventHandler<IKeyPhraseRecognizedEvent> KeyPhraseRecognized;
		public event EventHandler<IVoiceRecordEvent> CompletedProcessingVoice;
		public event EventHandler<IVoiceRecordEvent> StartedProcessingVoice;

		private const string TTSNamePreface = "misty-en-";		
		private IList<string> _audioTags = new List<string>();
		private ISpeechIntentManager _speechIntentManager;
		private AzureSpeechService _azureCognitive;
		private GoogleSpeechService _googleService;
		private bool _recording;
		private AssetWrapper _assetWrapper;
		private AzureSpeechParameters _azureSpeechParameters;
		private GoogleSpeechParameters _googleSpeechParameters;
		private TimeManager _timeManager;
		private IDictionary<string, KeyValuePair<bool, IList<string>>> _intentUtterances = new Dictionary<string, KeyValuePair<bool, IList<string>>>();
		private IList<string> _listeningCallbacks = new List<string>();
		private object _lockListenerData = new object();
		private bool _listenAborted;
		private int _silenceTimeout = 6000;
		private int _listenTimeout = 6000;
		private bool _keyPhraseTriggered;
		private bool _keyPhraseOn;
		private bool _processingAudioCallback;
		private SemaphoreSlim _keyPhraseOnSlim = new SemaphoreSlim(1, 1);

		private int _volume;
		public int Volume
		{
			get
			{
				return _volume;
			}
			set
			{
				if (_volume != value)
				{
					_volume = value;
					Robot.SetDefaultVolume(_volume, null);
				}
			}
		}

		public string GetLocaleName(string name)
		{
			if (CharacterParameters.AddLocaleToAudioNames)
			{
				switch (CharacterParameters.TextToSpeechService)
				{
					case "Google":
						name = _googleSpeechParameters.SpokenLanguage + _googleSpeechParameters.SpeakingVoice + _googleSpeechParameters.SpeakingGender?[0] + name;
						break;
					case "Azure":
						name = _azureSpeechParameters.TranslatedLanguage + _azureSpeechParameters.SpeakingVoice + name;
						break;
					default:
						name = TTSNamePreface + name;
						break;
				}
			}
			return AssetHelper.AddMissingWavExtension(name);
		}

		public void SetListenTimingMs(int listenTimeout, int silenceTimeout)
		{
			_listenTimeout = listenTimeout >= 1000 ? listenTimeout : 1000;
			_listenTimeout = silenceTimeout >= 1000 ? listenTimeout : 1000;			
		}

		public override async Task<bool> Initialize()
		{
			Robot.UnregisterEvent("KeyPhrase", null);
			Robot.UnregisterEvent("VoiceRecord", null);
			Robot.UnregisterEvent("CharacterAudioComplete", null);
			Robot.UnregisterEvent("CharacterTTSComplete", null);

			_azureSpeechParameters = CharacterParameters.AzureSpeechParameters;
			_googleSpeechParameters = CharacterParameters.GoogleSpeechParameters;
			_assetWrapper = new AssetWrapper(Robot);
			await RefreshAssetLists();
			
			Robot.SetNotificationSettings(true, false, true, CharacterParameters.ConversationGroup.KeyPhraseRecognizedAudio, null);
			Robot.GetVolume(ProcessVolumeResponse);

			_timeManager = new TimeManager(Robot, Parameters, CharacterParameters);

			if (_azureSpeechParameters != null && !string.IsNullOrWhiteSpace(_azureSpeechParameters.SubscriptionKey))
			{
				AzureServiceAuthorization servicesAuth = new AzureServiceAuthorization
				{
					Region = _azureSpeechParameters.Region,
					Endpoint = _azureSpeechParameters.Endpoint,
					SubscriptionKey = _azureSpeechParameters.SubscriptionKey
				};

				_azureCognitive = new AzureSpeechService(servicesAuth, Robot);
				_azureCognitive.SpeakingVoice = _azureSpeechParameters?.SpeakingVoice;
				_azureCognitive.SpokenLanguage = _azureSpeechParameters?.SpokenLanguage;
				_azureCognitive.TranslatedLanguage = _azureSpeechParameters?.TranslatedLanguage;
				_azureCognitive.ProfanitySetting = _azureSpeechParameters?.ProfanitySetting;

				_intentUtterances = CharacterParameters.ConversationGroup.IntentUtterances;
				_speechIntentManager = _speechIntentManager ?? new SpeechIntentManager(Robot, _intentUtterances);
			}

			if (_googleSpeechParameters != null && !string.IsNullOrWhiteSpace(_googleSpeechParameters.SubscriptionKey))
			{
				GoogleServiceAuthorization servicesAuth = new GoogleServiceAuthorization
				{
					STTEndpoint = _googleSpeechParameters?.STTEndpoint,
					TTSEndpoint = _googleSpeechParameters?.TTSEndpoint,
					SubscriptionKey = _googleSpeechParameters?.SubscriptionKey
				};

				_googleService = new GoogleSpeechService(servicesAuth, Robot);
				_googleService.SpeakingVoice = _googleSpeechParameters?.SpeakingVoice;
				_googleService.SpeakingGender = _googleSpeechParameters?.SpeakingGender;
				_googleService.SpokenLanguage = _googleSpeechParameters?.SpokenLanguage;
				
				_intentUtterances = CharacterParameters.ConversationGroup.IntentUtterances;
				_speechIntentManager = _speechIntentManager ?? new SpeechIntentManager(Robot, _intentUtterances);
			}

			LogEventDetails(Robot.RegisterVoiceRecordEvent(VoiceRecordCallback, 100, true, "VoiceRecord", null));
			LogEventDetails(Robot.RegisterKeyPhraseRecognizedEvent(KeyPhraseCallback, 100, true, "KeyPhrase", null));
			LogEventDetails(Robot.RegisterAudioPlayCompleteEvent(AudioCallback, 100, true, "CharacterAudioComplete", null));
			LogEventDetails(Robot.RegisterTextToSpeechCompleteEvent(TTSCallback, 100, true, "CharacterTTSComplete", null));
					
			return true;
		}
		
		/// <summary>
		/// Called when the POTENTIAL to start key phrase rec has been triggered (basically not listening already or speaking/playing audio)
		/// </summary>
		/// <param name="currentInteraction"></param>
		/// <param name="hasAudio"></param>
		/// <returns></returns>
		public async Task<bool> UpdateKeyPhraseRecognition(Interaction currentInteraction, bool hasAudio)
		{
			await _keyPhraseOnSlim.WaitAsync();
			try
			{
				if (_keyPhraseOn && (currentInteraction == null || hasAudio || !currentInteraction.AllowKeyPhraseRecognition))
				{
					await Robot.StopKeyPhraseRecognitionAsync();
					_keyPhraseOn = false;
					KeyPhraseRecognitionOn?.Invoke(this, false);
				}
				else if (currentInteraction != null && currentInteraction.AllowKeyPhraseRecognition && !hasAudio && (!_keyPhraseOn || _keyPhraseTriggered))
				{
					await Robot.StartKeyPhraseRecognitionAsync(false, true, (int)(currentInteraction.ListenTimeout * 1000), (int)(currentInteraction.SilenceTimeout * 1000), null);
					_keyPhraseOn = true;
					_keyPhraseTriggered = false;
					KeyPhraseRecognitionOn?.Invoke(this, true);
				}
			}
			catch { }
			finally
			{
				_keyPhraseOnSlim.Release();
			}

			return _keyPhraseOn;
		}

		public SpeechManager(IRobotMessenger misty, IDictionary<string, object> parameters, CharacterParameters characterParameters, ISpeechIntentManager speechIntentManager = null)
			: base(misty, parameters, characterParameters)
		{
			_speechIntentManager = speechIntentManager;
		}

		public void AbortListening(string audioName)
		{
			_listeningCallbacks.Remove(audioName);
			_listeningCallbacks.Remove(audioName + ".wav");
			_listenAborted = true;

			Robot.StopRecordingAudio(null);
		}

		private void ProcessVolumeResponse(IGetVolumeResponse volumeResponse)
		{
			if (volumeResponse?.Data != null && volumeResponse?.Data >= 0 && volumeResponse?.Data <= 100)
			{
				_volume = volumeResponse.Data;
			}
			else
			{
				_volume = 0;
			}
		}

		public async Task RefreshAssetLists()
		{
			await _assetWrapper.RefreshAssetLists();
		}
		
		public async virtual void Speak(AnimationRequest currentAnimation, Interaction currentInteraction)
		{
			try
			{
				bool textChanged = false;

				if (string.IsNullOrWhiteSpace(currentAnimation.Speak))
				{
					Robot.SkillLogger.Log("No text passed in to Speak command.");
					return;
				}
				
				if (string.IsNullOrWhiteSpace(currentAnimation.SpeakFileName))
				{
					currentAnimation.SpeakFileName = AssetHelper.MakeFileName(currentAnimation.Speak);
				}

				//This will save files with language and voice as part of the name
				if (CharacterParameters.AddLocaleToAudioNames)
				{
					currentAnimation.SpeakFileName = GetLocaleName(currentAnimation.SpeakFileName);
				}

				_listenAborted = false;

				if (CharacterParameters.TextToSpeechService == "Misty")
				{
					if (currentInteraction.StartListening && !string.IsNullOrWhiteSpace(currentAnimation.SpeakFileName))
					{
						lock (_lockListenerData)
						{
							_listeningCallbacks.Remove(currentAnimation.SpeakFileName);
							_listeningCallbacks.Add(currentAnimation.SpeakFileName);
						}
					}

					Robot.Speak(currentAnimation.Speak, true, currentAnimation.SpeakFileName, null);
					StartedSpeaking?.Invoke(this, currentAnimation.Speak);
					return;
				}

				if ((_azureCognitive != null && _azureCognitive.Authorized) || (_googleService != null && _googleService.Authorized))
				{
					string newText = currentAnimation.Speak;
					bool usingSSML = CharacterParameters.TextToSpeechService == "Azure" && TryGetSSMLText(currentAnimation.Speak, out  newText, currentAnimation);
					currentAnimation.Speak = newText ?? currentAnimation.Speak;

					currentAnimation.SpeakFileName = AssetHelper.AddMissingWavExtension(currentAnimation.SpeakFileName);

					if (currentInteraction.StartListening)
					{
						lock (_lockListenerData)
						{
							_listeningCallbacks.Remove(currentAnimation.SpeakFileName);
							_listeningCallbacks.Add(currentAnimation.SpeakFileName);
						}
					}

					StartedSpeaking?.Invoke(this, currentAnimation.Speak);
					if (!textChanged)
					{
						if (_audioTags.Contains(currentAnimation.SpeakFileName) ||
							(!CharacterParameters.RetranslateTTS &&
							_assetWrapper.AudioList.Where(x => AssetHelper.AreEqualAudioFilenames(x.Name, currentAnimation.SpeakFileName, CharacterParameters.AddLocaleToAudioNames)).Any())
						)
						{
							if (CharacterParameters.LogLevel == SkillLogLevel.Verbose || CharacterParameters.LogLevel == SkillLogLevel.Info)
							{
								Robot.SkillLogger.Log($"Speaking with existing audio file {currentAnimation.SpeakFileName} at volume {Volume}.");
								Robot.SkillLogger.Log(currentAnimation.Speak);
							}
							Robot.PlayAudio(currentAnimation.SpeakFileName, Volume, null);
						}
						else
						{
							if (CharacterParameters.LogLevel == SkillLogLevel.Verbose || CharacterParameters.LogLevel == SkillLogLevel.Info)
							{
								Robot.SkillLogger.Log($"Creating new audio file {currentAnimation.SpeakFileName} at volume {Volume}.");
								Robot.SkillLogger.Log(currentAnimation.Speak);
							}
                            
							switch (CharacterParameters.TextToSpeechService)
							{
								case "Google":
									await _googleService.Speak(currentAnimation.Speak, currentAnimation.SpeakFileName, Volume, usingSSML, 0);
									break;
								case "Azure":
								default:
									await _azureCognitive.Speak(currentAnimation.Speak, currentAnimation.SpeakFileName, Volume, usingSSML, (int)(currentAnimation.TrimAudioSilence*1000));
									break;
							}
							_audioTags.Add(currentAnimation.SpeakFileName);
						}
					}
					else
					{
						//Make a new file						
						switch (CharacterParameters.TextToSpeechService)
						{
							case "Google":
								//TODO Make configurable
								await _googleService.Speak(currentAnimation.Speak, currentAnimation.SpeakFileName ?? "TTSAudio", Volume, usingSSML, 0);
								break;
							case "Azure":
							default:
								await _azureCognitive.Speak(currentAnimation.Speak, currentAnimation.SpeakFileName ?? "TTSAudio", Volume, usingSSML, (int)currentAnimation.TrimAudioSilence * 1000);
								break;
						}
					}
				}
			}
			catch(Exception ex)
			{
				Robot.SkillLogger.Log("Failed processing Speak action in Character.", ex);
				StoppedSpeaking?.Invoke(this, null);
			}
		}


		private bool TryGetSSMLText(string text, out string newText, AnimationRequest animationRequest)
		{
			try
			{
				if (text.Trim().ToLower().Replace(" ", "").EndsWith("</speak>"))
				{
					//Don't adjust if already ssml
					newText = text;
					return true;
				}

				bool usingSSML = false;
				string[] startText = new string[2];
				string[] endText = new string[2];

				if (animationRequest.SpeechRate != 1.0)
				{
					startText[0] = $"<prosody rate=\"{animationRequest.SpeechRate}\">";
					endText[0] = "</prosody>";
					usingSSML = true;
				}
				else if (!string.IsNullOrWhiteSpace(animationRequest.SpeakingStyle))
				{
					startText[1] = $"<mstts:express-as style=\"{animationRequest.SpeakingStyle}\">";
					endText[1] = "</mstts:express-as>";
					usingSSML = true;
				}

				if (usingSSML)
				{
					/*
					Speak = "<?xml version=\"1.0\" encoding=\"utf-8\"?> <speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\">" +
					"<voice name=\"en-US-GuyNeural\"><prosody rate=\"2.0\">" +
					"California, the most populous US state and the first to implement a statewide lockdown to combat the coronavirus outbreak, is setting daily records this week for new cases as officials urge caution and dangle enforcement threats to try to curb the spikes." +
					"The virus is spreading at private gatherings in homes, and more young people are testing positive, Gov.Gavin Newsom said Wednesday.Infections at some prisons are raising concerns." +
					"</prosody></voice></speak>",*/

					newText = "<?xml version=\"1.0\" encoding=\"utf-8\"?> <speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xmlns:mstts=\"https://www.w3.org/2001/mstts\" xml:lang=\"en-US\">";
					
					//TODO allow pass in override by animation
					newText += $"<voice name=\"{(string.IsNullOrWhiteSpace(animationRequest.OverrideVoice) ? _azureSpeechParameters.SpeakingVoice : animationRequest.OverrideVoice)}\">";


					if (startText[0] != null)
					{
						newText += startText[0];
					}

					if (startText[1] != null)
					{
						newText += startText[1];
					}

					newText += text;

					if (endText[1] != null)
					{
						newText += endText[1];
					}

					if (endText[0] != null)
					{
						newText += endText[0];
					}

					newText += "</voice></speak>";

					return true;
				}

				newText = text;
				return false;
			}
			catch
			{
				newText = text;
				return false;
			}
		}

		protected void TTSCallback(ITextToSpeechCompleteEvent ttsComplete)
		{
			Robot.SkillLogger.Log($"TTS Callback: UtteranceId: {ttsComplete.UttteranceId}");

			AudioCallback(new AudioPlayCompleteEvent(ttsComplete.UttteranceId, -1));
		}
		
		protected void AudioCallback(IAudioPlayCompleteEvent audioComplete)
		{
			try
			{
				_recording = false;
				Robot.SkillLogger.Log($"Audio Callback. Name: {audioComplete.Name}");
				if(_processingAudioCallback)
				{
					return;
				}
				_processingAudioCallback = true;

				StoppedSpeaking?.Invoke(this, audioComplete);
				lock (_lockListenerData)
				{
					if (!_recording && !_listenAborted && (_listeningCallbacks.Remove(audioComplete.Name) || _listeningCallbacks.Remove(audioComplete.Name+".wav")))
					{
						_recording = true;

						Robot.SkillLogger.Log("Capture Speech called.");
						switch (CharacterParameters.SpeechRecognitionService)
						{
							//Add back in once new nuget released... foreshadowing ;)
							case "GoogleOnboard":
								//_ = Robot.CaptureSpeechGoogleAsync(false, _listenTimeout, _silenceTimeout, CharacterParameters.GoogleSpeechParameters.SubscriptionKey, CharacterParameters.GoogleSpeechParameters.SpokenLanguage);
								return;
							case "AzureOnboard":
								//_ = Robot.CaptureSpeechAzureAsync(false, _listenTimeout, _silenceTimeout, CharacterParameters.AzureSpeechParameters.SubscriptionKey, CharacterParameters.AzureSpeechParameters.SpokenLanguage);
								return;
							default:
								_ = Robot.CaptureSpeechAsync(false, true, _listenTimeout, _silenceTimeout, null);
								return;
						}
					}
				}
			}
			catch(Exception ex)
			{
				Robot.SkillLogger.Log("Failed processing audio callback in Character.", ex);
			}
			finally
			{
				_processingAudioCallback = false;
				StartedListening?.Invoke(this, DateTime.Now);
			}
		}

		private void KeyPhraseCallback(IKeyPhraseRecognizedEvent keyPhraseEvent)
		{ 
			try
			{
				Robot.SkillLogger.Log("Key Phrase Callback called, calling capture speech.");
				if (_recording)
				{
					return;
				}

				KeyPhraseRecognized?.Invoke(this, keyPhraseEvent);
				_keyPhraseTriggered = true;
				_recording = true;
				_ = Robot.CaptureSpeechAsync(false, true, _listenTimeout, _silenceTimeout, null);

				StartedListening?.Invoke(this, DateTime.Now);
				return;
			}
			catch (Exception ex)
			{
				Robot.SkillLogger.Log("Failed to process Voice Command event.", ex);
				SpeechIntent?.Invoke(this, new TriggerData("", ConversationConstants.HeardUnknownTrigger, Triggers.SpeechHeard));
			}
		}
		
		private async void VoiceRecordCallback(IVoiceRecordEvent voiceRecordEvent)
		{
			try
			{
				_recording = false;
				if (_listenAborted)
				{
					Robot.SkillLogger.Log("Voice Record Callback called while processing, ignoring.");
					return;
				}

				Robot.SkillLogger.Log("Voice Record Callback - processing");

				StartedProcessingVoice?.Invoke(this, voiceRecordEvent);

				StoppedListening?.Invoke(this, voiceRecordEvent);
				if (voiceRecordEvent.ErrorCode == 3)
				{
					Robot.SkillLogger.Log("Didn't hear anything or can no longer translate. Error 3");
					SpeechIntent?.Invoke(this, new TriggerData("", ConversationConstants.HeardNothingTrigger, Triggers.SpeechHeard));					
					return;
				}

				if (CharacterParameters.SpeechRecognitionService == "GoogleOnboard" ||
					CharacterParameters.SpeechRecognitionService == "AzureOnboard")
				{
					HandleSpeechResponse(voiceRecordEvent?.SpeechRecognitionResult);
					return;
				}

				IGetAudioResponse audioResponse;
				audioResponse = await Robot.GetAudioAsync("capture_Dialogue.wav", false);

				if(audioResponse.Status != ResponseStatus.Success)
				{
					Robot.SkillLogger.Log($"Failed to retrieve file 'capture_Dialogue.wav', received {audioResponse.Status}.  Ignoring speech intent.");
					//?? SpeechIntent?.Invoke(this, new TriggerData("", ConversationConstants.HeardNothingTrigger, Triggers.SpeechHeard));
					return;
				}
				else if (audioResponse?.Data?.Audio == null)
				{
					Robot.SkillLogger.Log("Couldn't find the audio file 'capture_Dialogue.wav'.");
					SpeechIntent?.Invoke(this, new TriggerData("", ConversationConstants.HeardNothingTrigger, Triggers.SpeechHeard));
					return;
				}
				else if ( audioResponse?.Data?.Audio.Count() == 0)
				{			
					Robot.SkillLogger.Log("Found empty audio file 'capture_Dialogue.wav'.");
					SpeechIntent?.Invoke(this, new TriggerData("", ConversationConstants.HeardNothingTrigger, Triggers.SpeechHeard));
					return;
				}

				SpeechToTextData description = new SpeechToTextData();
				
				switch (CharacterParameters.SpeechRecognitionService)
				{
					case "Google":
						description = await _googleService.TranslateAudioStream((byte[])audioResponse.Data.Audio);
						break;
					case "Azure":
					default:
						description = await _azureCognitive.TranslateAudioStream((byte[])audioResponse.Data.Audio);
						break;
				}

				HandleSpeechResponse(description.Text);
			}
			catch (Exception ex)
			{
				Robot.SkillLogger.Log("Failed to process Voice Command event.", ex);
				SpeechIntent?.Invoke(this, new TriggerData("", ConversationConstants.HeardUnknownTrigger, Triggers.SpeechHeard));
			}
			finally
			{
				CompletedProcessingVoice?.Invoke(this, voiceRecordEvent);
			}
		}

		private void HandleSpeechResponse(string text)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				string intent = _speechIntentManager.GetIntent(text, null);

				SpeechIntent?.Invoke(this, new TriggerData(text, intent, Triggers.SpeechHeard));
				Robot.SkillLogger.Log($"VoiceRecordCallback - Heard: '{text}' - Intent: {intent}");
			}
			else
			{
				Robot.SkillLogger.Log("Didn't hear anything or can no longer translate.");
				SpeechIntent?.Invoke(this, new TriggerData("", ConversationConstants.HeardNothingTrigger, Triggers.SpeechHeard));
			}
		}

		private bool _isDisposed = false;

		private void Dispose(bool disposing)
		{
			if (!_isDisposed)
			{
				if (disposing)
				{
					Robot.UnregisterAllEvents(null);
				}

				_isDisposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
	}
}
 