using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Helpers;
using WalletWasabi.Hwi2;
using WalletWasabi.Hwi2.Exceptions;
using WalletWasabi.Hwi2.Models;
using WalletWasabi.KeyManagement;
using Xunit;

namespace WalletWasabi.Tests.HwiTests.NoDeviceConnectedTests
{
	public class MockedDeviceTests
	{
		#region SharedVariables

		public TimeSpan ReasonableRequestTimeout { get; } = TimeSpan.FromMinutes(3);

		#endregion SharedVariables

		#region Tests

		[Theory]
		[MemberData(nameof(GetDifferentNetworkValues))]
		public async Task TrezorTMockTestsAsync(Network network)
		{
			var client = new HwiClient(network, new IMockHwiProcessBridge(HardwareWalletModels.TrezorT));

			using (var cts = new CancellationTokenSource(ReasonableRequestTimeout))
			{
				IEnumerable<HwiEnumerateEntry> enumerate = await client.EnumerateAsync(cts.Token);
				Assert.Single(enumerate);
				HwiEnumerateEntry entry = enumerate.Single();
				Assert.Equal(HardwareWalletVendors.Trezor, entry.Type);
				Assert.Equal("webusb: 001:4", entry.Path);
				Assert.False(entry.NeedsPassphraseSent);
				Assert.False(entry.NeedsPinSent);
				Assert.NotNull(entry.Error);
				Assert.NotEmpty(entry.Error);
				Assert.Equal(HwiErrorCode.DeviceNotInitialized, entry.Code);
				Assert.Null(entry.Fingerprint);

				var deviceType = entry.Type.Value;
				var devicePath = entry.Path;

				await client.WipeAsync(deviceType, devicePath, cts.Token);
				await client.SetupAsync(deviceType, devicePath, cts.Token);
				await client.RestoreAsync(deviceType, devicePath, cts.Token);

				// Trezor T doesn't support it.
				var backup = await Assert.ThrowsAsync<HwiException>(async () => await client.BackupAsync(deviceType, devicePath, cts.Token));
				Assert.Equal("The Trezor does not support creating a backup via software", backup.Message);
				Assert.Equal(HwiErrorCode.UnavailableAction, backup.ErrorCode);

				// Trezor T doesn't support it.
				var promptpin = await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
				Assert.Equal("The PIN has already been sent to this device", promptpin.Message);
				Assert.Equal(HwiErrorCode.DeviceAlreadyUnlocked, promptpin.ErrorCode);

				var sendpin = await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));
				Assert.Equal("The PIN has already been sent to this device", sendpin.Message);
				Assert.Equal(HwiErrorCode.DeviceAlreadyUnlocked, sendpin.ErrorCode);

				KeyPath keyPath1 = KeyManager.DefaultAccountKeyPath;
				KeyPath keyPath2 = KeyManager.DefaultAccountKeyPath.Derive(1);
				ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
				ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
				var expecteXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6DHjDx4gzLV37gJWMxYJAqyKRGN46MT61RHVizdU62cbVUYu9L95cXKzX62yJ2hPbN11EeprS8sSn8kj47skQBrmycCMzFEYBQSntVKFQ5M");
				var expecteXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6FJS1ne3STcKdQ9JLXNzZXidmCNZ9dxLiy7WVvsRkcmxjJsrDKJKEAXq4MGyEBM3vHEw2buqXezfNK5SNBrkwK7Fxjz1TW6xzRr2pUyMWFu");
				Assert.Equal(expecteXpub1, xpub1);
				Assert.Equal(expecteXpub2, xpub2);

				BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
				BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath2, cts.Token);

				BitcoinAddress expectedAddress1;
				BitcoinAddress expectedAddress2;
				if (network == Network.Main)
				{
					expectedAddress1 = BitcoinAddress.Create("bc1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7fdevah", Network.Main);
					expectedAddress2 = BitcoinAddress.Create("bc1qmaveee425a5xjkjcv7m6d4gth45jvtnj23fzyf", Network.Main);
				}
				else if (network == Network.TestNet)
				{
					expectedAddress1 = BitcoinAddress.Create("tb1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7rtzlxy", Network.TestNet);
					expectedAddress2 = BitcoinAddress.Create("tb1qmaveee425a5xjkjcv7m6d4gth45jvtnjqhj3l6", Network.TestNet);
				}
				else if (network == Network.RegTest)
				{
					expectedAddress1 = BitcoinAddress.Create("bcrt1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7pzmj3d", Network.RegTest);
					expectedAddress2 = BitcoinAddress.Create("bcrt1qmaveee425a5xjkjcv7m6d4gth45jvtnjz7tugn", Network.RegTest);
				}
				else
				{
					throw new NotSupportedException($"{network} not supported.");
				}

				Assert.Equal(expectedAddress1, address1);
				Assert.Equal(expectedAddress2, address2);
			}
		}

		#endregion Tests

		#region HelperMethods

		public static IEnumerable<object[]> GetDifferentNetworkValues()
		{
			var networks = new List<Network>
			{
				Network.Main,
				Network.TestNet,
				Network.RegTest
			};

			foreach (Network network in networks)
			{
				yield return new object[] { network };
			}
		}

		#endregion HelperMethods
	}
}
