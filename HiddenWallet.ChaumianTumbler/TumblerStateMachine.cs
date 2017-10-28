﻿using HiddenWallet.ChaumianCoinJoin;
using HiddenWallet.ChaumianTumbler.Denomination;
using HiddenWallet.ChaumianTumbler.Models;
using HiddenWallet.WebClients.SmartBit;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HiddenWallet.ChaumianTumbler
{
    public class TumblerStateMachine : IDisposable
    {
		public TumblerPhase Phase { get; private set; } = TumblerPhase.InputRegistration;
		public Money Denomination { get; private set; }
		private SmartBitClient SmartBitClient { get; }

		private TumblerPhaseBroadcaster _broadcaster = TumblerPhaseBroadcaster.Instance;

		private CancellationTokenSource _ctsPhaseCancel = new CancellationTokenSource();

		public TumblerStateMachine()
		{
			SmartBitClient = new SmartBitClient(Network.Main);
		}

		public void UpdatePhase(TumblerPhase phase)
		{
			if (phase == Phase) return;

			Phase = phase;
			_ctsPhaseCancel.Cancel();
			_ctsPhaseCancel = new CancellationTokenSource();
			var broadcast = new PhaseChangeBroadcast { NewPhase = phase.ToString(), Message = "" };
			_broadcaster.Broadcast(broadcast);
			Console.WriteLine($"NEW PHASE: {phase}");
		}

		public void AdvancePhase()
		{
			switch (Phase)
			{
				case TumblerPhase.InputRegistration:
					{
						UpdatePhase(TumblerPhase.InputConfirmation);
						break;
					}
				case TumblerPhase.InputConfirmation:
					{
						UpdatePhase(TumblerPhase.OutputRegistration);
						break;
					}
				case TumblerPhase.OutputRegistration:
					{
						UpdatePhase(TumblerPhase.Signing);
						break;
					}
				case TumblerPhase.Signing:
					{
						UpdatePhase(TumblerPhase.InputRegistration);
						break;
					}
				default:
					{
						throw new NotSupportedException("This should never happen");
					}
			}
		}

		public async Task StartAsync(CancellationToken cancel)
		{
			while (true)
			{
				try
				{
					if (cancel.IsCancellationRequested) return;

					switch(Phase)
					{
						case TumblerPhase.InputRegistration:
							{
								await SetDenominationAsync(cancel);

								using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel, _ctsPhaseCancel.Token))
								{
									await Task.Delay(TimeSpan.FromSeconds((int)Global.Config.InputRegistrationPhaseTimeoutInSeconds), cts.Token).ContinueWith(t => { });
								}
								AdvancePhase();
								break;
							}
						case TumblerPhase.InputConfirmation:
							{
								using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel, _ctsPhaseCancel.Token))
								{
									await Task.Delay(TimeSpan.FromSeconds((int)Global.Config.InputConfirmationPhaseTimeoutInSeconds), cts.Token).ContinueWith(t => { });
								}
								AdvancePhase();
								break;
							}
						case TumblerPhase.OutputRegistration:
							{
								using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel, _ctsPhaseCancel.Token))
								{
									await Task.Delay(TimeSpan.FromSeconds((int)Global.Config.OutputRegistrationPhaseTimeoutInSeconds), cts.Token).ContinueWith(t => { });
								}
								AdvancePhase();
								break;
							}
						case TumblerPhase.Signing:
							{
								using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel, _ctsPhaseCancel.Token))
								{
									await Task.Delay(TimeSpan.FromSeconds((int)Global.Config.SigningPhaseTimeoutInSeconds), cts.Token).ContinueWith(t => { });
								}
								AdvancePhase();
								break;
							}
						default:
							{
								throw new NotSupportedException("This should never happen");
							}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Ignoring {nameof(TumblerStateMachine)} exception: {ex}");
				}
			}
		}

		private async Task SetDenominationAsync(CancellationToken cancel)
		{
			if (Global.Config.DenominationAlgorithm == DenominationAlgorithm.FixedUSD)
			{
				try
				{
					var exchangeRates = await SmartBitClient.GetExchangeRatesAsync(cancel);
					decimal price = exchangeRates.Single(x => x.Code == "USD").Rate;
					decimal denominationUSD = (decimal)Global.Config.DenominationUSD;
					decimal denominationBTC = denominationUSD / price;
					Denomination = new Money(denominationBTC, MoneyUnit.BTC);
				}
				catch
				{
					Denomination = Global.Config.DenominationBTC;
				}
			}
			else if (Global.Config.DenominationAlgorithm == DenominationAlgorithm.FixedBTC)
			{
				Denomination = Global.Config.DenominationBTC;
			}
			else
			{
				throw new NotSupportedException(Global.Config.DenominationAlgorithm.ToString());
			}
		}

		#region IDisposable Support
		private bool _disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					_ctsPhaseCancel?.Dispose();
					SmartBitClient?.Dispose();
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				_disposedValue = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~TumblerStateMachine() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion
	}
}
