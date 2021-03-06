﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Specialized;
using System.ComponentModel;

using Floe.Net;
using Floe.Audio;
using Floe.Interop;

namespace Floe.UI
{
	public partial class VoiceControl : UserControl, IDisposable
	{
		private void voice_Error(object sender, ErrorEventArgs e)
		{
			System.Diagnostics.Debug.WriteLine(e.Exception.ToString());
			MessageBox.Show("An error occurred and voice chat must stop: " + e.Exception.Message);
			this.IsChatting = false;
		}

		private void session_CtcpCommandReceived(object sender, CtcpEventArgs e)
		{
			VoiceCodec codec;
			int quality;
			IPAddress pubAddress, prvAddress;
			int pubPort, prvPort;

			if (!this.IsChatting)
			{
				return;
			}

			if (string.Compare(e.Command.Command, "VCHAT", StringComparison.OrdinalIgnoreCase) == 0)
			{
				if (e.Command.Arguments.Length == 7 &&
				string.Compare(e.Command.Arguments[0], "START", StringComparison.OrdinalIgnoreCase) == 0 &&
				(!e.To.IsChannel || e.To.Equals(_target)) &&
				Enum.TryParse(e.Command.Arguments[1], out codec) &&
				int.TryParse(e.Command.Arguments[2], out quality) &&
				IPAddress.TryParse(e.Command.Arguments[3], out pubAddress) &&
				int.TryParse(e.Command.Arguments[4], out pubPort) &&
				IPAddress.TryParse(e.Command.Arguments[5], out prvAddress) &&
				int.TryParse(e.Command.Arguments[6], out prvPort) &&
				pubPort > 0 && pubPort <= ushort.MaxValue &&
				prvPort > 0 && prvPort <= ushort.MaxValue)
				{
					// if they're behind the same NAT, use the local address
					if (pubAddress.Equals(_publicEndPoint.Address))
					{
						pubAddress = prvAddress;
						pubPort = prvPort;
					}
					this.AddPeer(e.From.Nickname, codec, quality, new IPEndPoint(pubAddress, pubPort));
					if (!e.IsResponse && e.To.IsChannel)
					{
						_session.SendCtcp(new IrcTarget(e.From), new CtcpCommand("VCHAT", "START",
							VoiceCodec.Gsm610.ToString(),
							App.Settings.Current.Voice.Quality.ToString(),
							_publicEndPoint.Address.ToString(),
							_publicEndPoint.Port.ToString(),
							_session.InternalAddress.ToString(),
							_voice.LocalEndPoint.Port.ToString()), true);
					}
				}
				else if (e.Command.Arguments.Length == 1 &&
					string.Compare(e.Command.Arguments[0], "STOP", StringComparison.OrdinalIgnoreCase) == 0)
				{
					this.RemovePeer(e.From.Nickname);
				}
			}
		}

		private void RawInput_ButtonDown(object sender, RawInputEventArgs e)
		{
			if (e.Button == App.Settings.Current.Voice.TalkKey)
			{
				_isTalkKeyDown = true;
			}
		}

		private void RawInput_ButtonUp(object sender, RawInputEventArgs e)
		{
			if (e.Button == App.Settings.Current.Voice.TalkKey)
			{
				_isTalkKeyDown = false;
			}
		}

		private void nickList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action == NotifyCollectionChangedAction.Remove &&
				e.OldItems.Count > 0)
			{
				var endpoint = this.FindEndPoint(((NicknameItem)e.OldItems[0]));
				if (endpoint != null)
				{
					this.RemovePeer(this.FindEndPoint(((NicknameItem)e.OldItems[0])));
				}
			}
		}

		private void Voice_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case "PlaybackVolume":
					_voice.OutputVolume = App.Settings.Current.Voice.PlaybackVolume;
					break;
				case "InputGain":
					_voice.InputGain = App.Settings.Current.Voice.InputGain;
					break;
				case "OutputGain":
					_voice.OutputGain = App.Settings.Current.Voice.OutputGain;
					break;
			}
		}

		private bool TransmitPredicate()
		{
			bool isTransmitting = false;

			if (App.Settings.Current.Voice.PushToTalk)
			{
				isTransmitting = _isTalkKeyDown;
			}
			else
			{
				if (_voice.InputLevel >= App.Settings.Current.Voice.TalkLevel)
				{
					_lastTransmit = DateTime.Now.Ticks;
				}
				if (DateTime.Now.Ticks - _lastTransmit < TrailTime)
				{
					isTransmitting = true;
				}
			}
			if (isTransmitting != _isTransmitting)
			{
				_isTransmitting = isTransmitting;
				this.Dispatcher.BeginInvoke((Action)(() => SetIsTalking(_self, (this.IsTransmitting = _isTransmitting))));
			}

			var ticks = DateTime.Now.Ticks;
			foreach (var peer in _peers.Values)
			{
				if (peer.IsTalking && ticks - peer.LastTransmit > TrailTime)
				{
					peer.IsTalking = false;
					this.Dispatcher.BeginInvoke((Action<NicknameItem>)((o) => SetIsTalking((NicknameItem)o, false)), peer.User);
				}
			}

			return isTransmitting;
		}

		private bool ReceivePredicate(IPEndPoint endpoint)
		{
			var peer = _peers[endpoint];
			peer.LastTransmit = DateTime.Now.Ticks;
			if (!peer.IsTalking)
			{
				peer.IsTalking = true;
				this.Dispatcher.BeginInvoke((Action<NicknameItem>)((o) => SetIsTalking((NicknameItem)o, true)), peer.User);
			}
			return !peer.IsMuted;
		}

		private void SubscribeEvents()
		{
			_voice.Error += voice_Error;
			_session.CtcpCommandReceived += session_CtcpCommandReceived;
			_nickList.CollectionChanged += nickList_CollectionChanged;
			RawInput.ButtonDown += RawInput_ButtonDown;
			RawInput.ButtonUp += RawInput_ButtonUp;
			App.Settings.Current.Voice.PropertyChanged += Voice_PropertyChanged;
		}

		private void UnsubscribeEvents()
		{
			_voice.Error -= voice_Error;
			_session.CtcpCommandReceived -= session_CtcpCommandReceived;
			_nickList.CollectionChanged -= nickList_CollectionChanged;
			RawInput.ButtonDown -= RawInput_ButtonDown;
			RawInput.ButtonUp -= RawInput_ButtonUp;
			App.Settings.Current.Voice.PropertyChanged -= Voice_PropertyChanged;
		}
	}
}
