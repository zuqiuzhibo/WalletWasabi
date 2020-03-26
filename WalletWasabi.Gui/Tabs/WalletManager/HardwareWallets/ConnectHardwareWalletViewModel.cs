using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using AvalonStudio.Extensibility;
using NBitcoin;
using Newtonsoft.Json.Linq;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Gui.Controls.WalletExplorer;
using WalletWasabi.Gui.Helpers;
using WalletWasabi.Gui.Models;
using WalletWasabi.Gui.Models.StatusBarStatuses;
using WalletWasabi.Gui.Tabs.WalletManager.LoadWallets;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Helpers;
using WalletWasabi.Hwi;
using WalletWasabi.Hwi.Models;
using WalletWasabi.Logging;

namespace WalletWasabi.Gui.Tabs.WalletManager.HardwareWallets
{
	public class ConnectHardwareWalletViewModel : CategoryViewModel
	{
		private ObservableCollection<LoadWalletEntry> _wallets;
		private LoadWalletEntry _selectedWallet;
		private bool _isWalletSelected;
		private bool _isWalletOpened;
		private bool _canLoadWallet;
		private bool _isBusy;
		private bool _isHardwareBusy;
		private string _loadButtonText;
		private bool _isHwWalletSearchTextVisible;

		public ConnectHardwareWalletViewModel(WalletManagerViewModel owner) : base("Hardware Wallet")
		{
			Global = Locator.Current.GetService<Global>();
			Owner = owner;
			Wallets = new ObservableCollection<LoadWalletEntry>();
			IsHwWalletSearchTextVisible = false;

			this.WhenAnyValue(x => x.SelectedWallet)
				.Subscribe(_ => TrySetWalletStates());

			this.WhenAnyValue(x => x.IsWalletOpened)
				.Subscribe(_ => TrySetWalletStates());

			this.WhenAnyValue(x => x.IsBusy)
				.Subscribe(_ => TrySetWalletStates());

			this.WhenAnyValue(x => x.IsBusy)
				.Subscribe(_ => TrySetWalletStates());

			LoadCommand = ReactiveCommand.CreateFromTask(LoadWalletAsync, this.WhenAnyValue(x => x.CanLoadWallet));
			ImportColdcardCommand = ReactiveCommand.CreateFromTask(async () =>
			{
				var ofd = new OpenFileDialog
				{
					AllowMultiple = false,
					Title = "Import Coldcard"
				};

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{
					ofd.Directory = Path.Combine("/media", Environment.UserName);
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					ofd.Directory = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
				}

				var window = (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime).MainWindow;
				var selected = await ofd.ShowAsync(window, fallBack: true);
				if (selected is { } && selected.Any())
				{
					var path = selected.First();
					var jsonString = await File.ReadAllTextAsync(path);
					var json = JObject.Parse(jsonString);
					var xpubString = json["ExtPubKey"].ToString();
					var mfpString = json["MasterFingerprint"].ToString();

					// https://github.com/zkSNACKs/WalletWasabi/pull/1663#issuecomment-508073066
					// Coldcard 2.1.0 improperly implemented Wasabi skeleton fingerprint at first, so we must reverse byte order.
					// The solution was to add a ColdCardFirmwareVersion json field from 2.1.1 and correct the one generated by 2.1.0.
					var coldCardVersionString = json["ColdCardFirmwareVersion"]?.ToString();
					var reverseByteOrder = false;
					if (coldCardVersionString is null)
					{
						reverseByteOrder = true;
					}
					else
					{
						Version coldCardVersion = new Version(coldCardVersionString);

						if (coldCardVersion == new Version("2.1.0")) // Should never happen though.
						{
							reverseByteOrder = true;
						}
					}

					var bytes = ByteHelpers.FromHex(Guard.NotNullOrEmptyOrWhitespace(nameof(mfpString), mfpString, trim: true));
					HDFingerprint mfp = reverseByteOrder ? new HDFingerprint(bytes.Reverse().ToArray()) : new HDFingerprint(bytes);

					ExtPubKey extPubKey = NBitcoinHelpers.BetterParseExtPubKey(xpubString);

					Logger.LogInfo("Creating a new wallet file.");
					var walletName = Global.WalletManager.WalletDirectories.GetNextWalletName("Coldcard");
					var walletFullPath = Global.WalletManager.WalletDirectories.GetWalletFilePaths(walletName).walletFilePath;
					KeyManager.CreateNewHardwareWalletWatchOnly(mfp, extPubKey, walletFullPath);
					owner.SelectLoadWallet();
				}
			});
			EnumerateHardwareWalletsCommand = ReactiveCommand.CreateFromTask(async () => await EnumerateIfHardwareWalletsAsync());
			OpenBrowserCommand = ReactiveCommand.CreateFromTask<string>(IoHelpers.OpenBrowserAsync);

			Observable
				.Merge(LoadCommand.ThrownExceptions)
				.Merge(OpenBrowserCommand.ThrownExceptions)
				.Merge(ImportColdcardCommand.ThrownExceptions)
				.Merge(EnumerateHardwareWalletsCommand.ThrownExceptions)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Subscribe(ex =>
				{
					Logger.LogError(ex);
					NotificationHelpers.Error(ex.ToUserFriendlyString());
				});
		}

		public bool IsHwWalletSearchTextVisible
		{
			get => _isHwWalletSearchTextVisible;
			set => this.RaiseAndSetIfChanged(ref _isHwWalletSearchTextVisible, value);
		}

		public ObservableCollection<LoadWalletEntry> Wallets
		{
			get => _wallets;
			set => this.RaiseAndSetIfChanged(ref _wallets, value);
		}

		public LoadWalletEntry SelectedWallet
		{
			get => _selectedWallet;
			set => this.RaiseAndSetIfChanged(ref _selectedWallet, value);
		}

		public bool IsWalletSelected
		{
			get => _isWalletSelected;
			set => this.RaiseAndSetIfChanged(ref _isWalletSelected, value);
		}

		public bool IsWalletOpened
		{
			get => _isWalletOpened;
			set => this.RaiseAndSetIfChanged(ref _isWalletOpened, value);
		}

		public string LoadButtonText
		{
			get => _loadButtonText;
			set => this.RaiseAndSetIfChanged(ref _loadButtonText, value);
		}

		public bool CanLoadWallet
		{
			get => _canLoadWallet;
			set => this.RaiseAndSetIfChanged(ref _canLoadWallet, value);
		}

		public bool IsBusy
		{
			get => _isBusy;
			set => this.RaiseAndSetIfChanged(ref _isBusy, value);
		}

		public bool IsHardwareBusy
		{
			get => _isHardwareBusy;
			set => this.RaiseAndSetIfChanged(ref _isHardwareBusy, value);
		}

		public ReactiveCommand<Unit, Unit> LoadCommand { get; }
		public ReactiveCommand<Unit, Unit> ImportColdcardCommand { get; set; }
		public ReactiveCommand<Unit, Unit> EnumerateHardwareWalletsCommand { get; set; }
		public ReactiveCommand<string, Unit> OpenBrowserCommand { get; }
		public string UDevRulesLink => "https://github.com/bitcoin-core/HWI/tree/master/hwilib/udev";
		public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
		private Global Global { get; }
		private WalletManagerViewModel Owner { get; }

		public void SetLoadButtonText()
		{
			var text = "Load Wallet";
			if (IsHardwareBusy)
			{
				text = "Waiting for Hardware Wallet...";
			}
			else if (IsBusy)
			{
				text = "Loading...";
			}
			else
			{
				// If the hardware wallet was not initialized, then make the button say Setup, not Load.
				// If pin is needed, then make the button say Send Pin instead.

				if (SelectedWallet?.HardwareWalletInfo is { })
				{
					if (!SelectedWallet.HardwareWalletInfo.IsInitialized())
					{
						text = "Setup Wallet";
					}

					if (SelectedWallet.HardwareWalletInfo.NeedsPinSent is true)
					{
						text = "Send PIN";
					}
				}
			}

			LoadButtonText = text;
		}

		public async Task<KeyManager> LoadKeyManagerAsync()
		{
			try
			{
				var selectedWallet = SelectedWallet;
				if (selectedWallet is null)
				{
					NotificationHelpers.Warning("No wallet selected.");
					return null;
				}

				var walletName = selectedWallet.WalletName;

				var client = new HwiClient(Global.Network);

				if (selectedWallet.HardwareWalletInfo is null)
				{
					NotificationHelpers.Warning("No hardware wallet detected.");
					return null;
				}

				if (!selectedWallet.HardwareWalletInfo.IsInitialized())
				{
					try
					{
						IsHardwareBusy = true;
						MainWindowViewModel.Instance.StatusBar.TryAddStatus(StatusType.SettingUpHardwareWallet);

						// Setup may take a while for users to write down stuff.
						using (var ctsSetup = new CancellationTokenSource(TimeSpan.FromMinutes(21)))
						{
							// Trezor T doesn't require interactive mode.
							if (selectedWallet.HardwareWalletInfo.Model == HardwareWalletModels.Trezor_T
							|| selectedWallet.HardwareWalletInfo.Model == HardwareWalletModels.Trezor_T_Simulator)
							{
								await client.SetupAsync(selectedWallet.HardwareWalletInfo.Model, selectedWallet.HardwareWalletInfo.Path, false, ctsSetup.Token);
							}
							else
							{
								await client.SetupAsync(selectedWallet.HardwareWalletInfo.Model, selectedWallet.HardwareWalletInfo.Path, true, ctsSetup.Token);
							}
						}

						MainWindowViewModel.Instance.StatusBar.TryAddStatus(StatusType.ConnectingToHardwareWallet);
						await EnumerateIfHardwareWalletsAsync();
					}
					finally
					{
						IsHardwareBusy = false;
						MainWindowViewModel.Instance.StatusBar.TryRemoveStatus(StatusType.SettingUpHardwareWallet, StatusType.ConnectingToHardwareWallet);
					}

					return await LoadKeyManagerAsync();
				}
				else if (selectedWallet.HardwareWalletInfo.NeedsPinSent is true)
				{
					await PinPadViewModel.UnlockAsync(selectedWallet.HardwareWalletInfo);

					var p = selectedWallet.HardwareWalletInfo.Path;
					var t = selectedWallet.HardwareWalletInfo.Model;
					await EnumerateIfHardwareWalletsAsync();
					selectedWallet = Wallets.FirstOrDefault(x => x.HardwareWalletInfo.Model == t && x.HardwareWalletInfo.Path == p);
					if (selectedWallet is null)
					{
						NotificationHelpers.Warning("Could not find the hardware wallet. Did you disconnect it?");
						return null;
					}
					else
					{
						SelectedWallet = selectedWallet;
					}

					if (!selectedWallet.HardwareWalletInfo.IsInitialized())
					{
						NotificationHelpers.Warning("Hardware wallet is not initialized.");
						return null;
					}

					if (selectedWallet.HardwareWalletInfo.NeedsPinSent is true)
					{
						NotificationHelpers.Warning("Hardware wallet needs a PIN to be sent.");
						return null;
					}
				}

				ExtPubKey extPubKey;
				var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
				try
				{
					MainWindowViewModel.Instance.StatusBar.TryAddStatus(StatusType.AcquiringXpubFromHardwareWallet);
					extPubKey = await client.GetXpubAsync(selectedWallet.HardwareWalletInfo.Model, selectedWallet.HardwareWalletInfo.Path, KeyManager.DefaultAccountKeyPath, cts.Token);
				}
				finally
				{
					cts?.Dispose();
					MainWindowViewModel.Instance.StatusBar.TryRemoveStatus(StatusType.AcquiringXpubFromHardwareWallet);
				}

				Logger.LogInfo("Hardware wallet was not used previously on this computer. Creating a new wallet file.");

				if (TryFindWalletByExtPubKey(extPubKey, out string wn))
				{
					walletName = wn;
				}
				else
				{
					var prefix = selectedWallet.HardwareWalletInfo is null ? "HardwareWallet" : selectedWallet.HardwareWalletInfo.Model.ToString();

					walletName = Global.WalletManager.WalletDirectories.GetNextWalletName(prefix);
					var path = Global.WalletManager.WalletDirectories.GetWalletFilePaths(walletName).walletFilePath;

					// Get xpub should had triggered passphrase request, so the fingerprint should be available here.
					if (!selectedWallet.HardwareWalletInfo.Fingerprint.HasValue)
					{
						await EnumerateIfHardwareWalletsAsync();
						selectedWallet = Wallets.FirstOrDefault(x => x.HardwareWalletInfo.Model == selectedWallet.HardwareWalletInfo.Model && x.HardwareWalletInfo.Path == selectedWallet.HardwareWalletInfo.Path);
					}
					if (!selectedWallet.HardwareWalletInfo.Fingerprint.HasValue)
					{
						throw new InvalidOperationException("Hardware wallet did not provide fingerprint.");
					}
					KeyManager.CreateNewHardwareWalletWatchOnly(selectedWallet.HardwareWalletInfo.Fingerprint.Value, extPubKey, path);
				}

				KeyManager keyManager = Global.WalletManager.GetWalletByName(walletName).KeyManager;

				return keyManager;
			}
			catch (Exception ex)
			{
				try
				{
					await EnumerateIfHardwareWalletsAsync();
				}
				catch (Exception ex2)
				{
					Logger.LogError(ex2);
				}

				// Initialization failed.
				NotificationHelpers.Error(ex.ToUserFriendlyString());
				Logger.LogError(ex);

				return null;
			}
		}

		public async Task LoadWalletAsync()
		{
			try
			{
				IsBusy = true;

				var keyManager = await LoadKeyManagerAsync();
				if (keyManager is null)
				{
					return;
				}

				try
				{
					bool isSuccessful = await Global.WaitForInitializationCompletedAsync(CancellationToken.None);
					if (!isSuccessful)
					{
						return;
					}

					var wallet = await Task.Run(async () => await Global.WalletManager.StartWalletAsync(keyManager));
					// Successfully initialized.
					Owner.OnClose();
				}
				catch (Exception ex)
				{
					// Initialization failed.
					NotificationHelpers.Error(ex.ToUserFriendlyString());
					if (!(ex is OperationCanceledException))
					{
						Logger.LogError(ex);
					}
				}
			}
			finally
			{
				IsBusy = false;
			}
		}

		private bool TrySetWalletStates()
		{
			try
			{
				if (SelectedWallet is null)
				{
					SelectedWallet = Wallets.FirstOrDefault();
				}

				IsWalletSelected = SelectedWallet is { };

				if (Global.WalletManager.AnyWallet())
				{
					IsWalletOpened = true;
					CanLoadWallet = false;
				}
				else
				{
					IsWalletOpened = false;

					// If not busy loading.
					// And wallet is selected.
					// And no wallet is opened.
					CanLoadWallet = !IsBusy && IsWalletSelected;
				}

				SetLoadButtonText();
				return true;
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}

			return false;
		}

		protected async Task EnumerateIfHardwareWalletsAsync()
		{
			var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
			IsHwWalletSearchTextVisible = true;
			try
			{
				var client = new HwiClient(Global.Network);
				var devices = await client.EnumerateAsync(cts.Token);

				Wallets.Clear();
				foreach (var dev in devices)
				{
					var walletEntry = new LoadWalletEntry(dev);
					Wallets.Add(walletEntry);
				}
				TrySetWalletStates();
			}
			finally
			{
				IsHwWalletSearchTextVisible = false;
				cts.Dispose();
			}
		}

		private bool TryFindWalletByExtPubKey(ExtPubKey extPubKey, out string walletName)
		{
			walletName = Global.WalletManager.WalletDirectories
				.EnumerateWalletFiles(includeBackupDir: true)
				.FirstOrDefault(fi => KeyManager.TryGetExtPubKeyFromFile(fi.FullName, out ExtPubKey epk) && epk == extPubKey)
				?.Name;

			return walletName is { };
		}
	}
}
