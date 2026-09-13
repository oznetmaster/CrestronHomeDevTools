// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DiscoveryTests
	{
	[Test]
	public void ParsesNativeSystemNameAndRejectsUnrelatedPacket ()
		{
		var packet = new byte[266];
		packet[0] = 0x15;
		Encoding.UTF8.GetBytes ("Development Home").CopyTo (packet, 10);
		Assert.That (NativeDiscovery.ParseName (packet), Is.EqualTo ("Development Home"));
		packet[0] = 0x14;
		Assert.That (NativeDiscovery.ParseName (packet), Is.Null);
		}

	[TestCase ("{}")]
	[TestCase ("{\"SystemType\":\"TouchPanel\",\"ProgramVersion\":\"1\"}")]
	[TestCase ("{\"ConfigurationSchema\":{\"Devices\":1},\"SystemType\":\"Generic\",\"ProgramVersion\":\"1\"}")]
	public void DiscoveryDoesNotTreatEveryCrestronDeviceAsHomeProcessor (string input)
		{
		using var json = JsonDocument.Parse (input);
		Assert.That (ProcessorDiscovery.ParseIdentification ("name", "192.0.2.1", json.RootElement), Is.Null);
		}

	[Test]
	public void IdentifiesHomeApiWithoutCredentials ()
		{
		using var json = JsonDocument.Parse ("{\"ConfigurationSchema\":{\"Devices\":1,\"House\":1},\"SystemType\":\"MC4-R\",\"ProgramVersion\":\"4.011.0322\"}");
		Assert.That (ProcessorDiscovery.ParseIdentification ("Development", "192.0.2.1", json.RootElement), Is.EqualTo (new DiscoveredProcessor ("Development", "192.0.2.1", "MC4-R", "4.011.0322")));
		}

	[Test]
	public void NameResolutionUsesCurrentAddressAndRejectsAmbiguousNames ()
		{
		var first = new DiscoveredProcessor ("Development", "192.0.2.5", "MC4-R", "4");
		Assert.That (ProcessorDiscovery.SelectByName ([first], "development").Address, Is.EqualTo ("192.0.2.5"));
		Assert.Throws<InvalidOperationException> (() => ProcessorDiscovery.SelectByName ([first, first with { Address = "192.0.2.6" }], "Development"));
		Assert.Throws<InvalidOperationException> (() => ProcessorDiscovery.SelectByName ([first], "Missing"));
		}
	}