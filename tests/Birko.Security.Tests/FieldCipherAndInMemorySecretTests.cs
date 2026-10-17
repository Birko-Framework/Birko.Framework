using System.Security.Cryptography;
using Birko.Security;
using Birko.Security.Encryption;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// TASK-532 — <see cref="FieldCipher"/> (KDF-derived key, versioned envelope with a key id, caller's associated data,
/// AES-GCM, rotation) and <see cref="InMemorySecretProvider"/>. DraCode's cipher it replaces keyed with SHA256(master),
/// carried no key id and bound no context, so a rotated master lost every value and a ciphertext moved between rows
/// still decrypted.
/// </summary>
public class FieldCipherAndInMemorySecretTests
{
    private static readonly byte[] MasterA = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] MasterB = RandomNumberGenerator.GetBytes(32);

    private static FieldCipher Cipher(string current = "a", params (string Id, byte[] Master)[] keys)
        => new(current, (keys.Length == 0 ? [("a", MasterA)] : keys).ToDictionary(k => k.Id, k => k.Master));

    // ── round trip and envelope ──

    [Theory]
    [InlineData("sk-live-1234567890")]
    [InlineData("")]
    [InlineData("ünïcødé ✓ and a long value ")]
    public void A_value_round_trips(string plaintext)
    {
        var cipher = Cipher();
        var envelope = cipher.Encrypt(plaintext, "row/1/apiKey");

        envelope.Should().StartWith("bfc1:a:");
        FieldCipher.IsEnvelope(envelope).Should().BeTrue();
        envelope.Should().NotContain(plaintext.Length > 0 ? plaintext : "\u0001");
        cipher.Decrypt(envelope, "row/1/apiKey").Should().Be(plaintext);
    }

    [Fact]
    public void The_same_value_encrypts_differently_each_time()
    {
        var cipher = Cipher();
        cipher.Encrypt("x", "ctx").Should().NotBe(cipher.Encrypt("x", "ctx"));
    }

    [Fact]
    public void Another_cipher_over_the_same_master_reads_the_value()
    {
        var envelope = Cipher().Encrypt("secret", "ctx");
        Cipher().Decrypt(envelope, "ctx").Should().Be("secret");
    }

    // ── associated data and tampering ──

    [Fact]
    public void A_value_moved_to_another_row_does_not_decrypt()
    {
        var cipher = Cipher();
        var envelope = cipher.Encrypt("secret", "provider-config/row-1/apiKey");

        var act = () => cipher.Decrypt(envelope, "provider-config/row-2/apiKey");
        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData(0)]   // nonce
    [InlineData(14)]  // ciphertext
    [InlineData(-1)]  // tag
    public void A_tampered_byte_is_detected(int position)
    {
        var cipher = Cipher();
        var envelope = cipher.Encrypt("secret", "ctx");
        var head = envelope[..(envelope.LastIndexOf(':') + 1)];
        var b64 = envelope[head.Length..].Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
        bytes[position < 0 ? bytes.Length + position : position] ^= 0x01;
        var tampered = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var act = () => cipher.Decrypt(head + tampered, "ctx");
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Relabelling_the_key_id_is_detected()
    {
        // Both keys held: the swapped label names a real key, so only the authenticated header can catch it.
        var cipher = Cipher("a", ("a", MasterA), ("b", MasterB));
        var envelope = cipher.Encrypt("secret", "ctx");

        var act = () => cipher.Decrypt(envelope.Replace("bfc1:a:", "bfc1:b:"), "ctx");
        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("plaintext")]
    [InlineData("bfc2:a:AAAA")]
    [InlineData("bfc1:bad id:AAAA")]
    [InlineData("bfc1:a:not*base64")]
    public void Something_that_is_not_an_envelope_is_refused(string value)
    {
        var act = () => Cipher().Decrypt(value, "ctx");
        act.Should().Throw<FormatException>();
    }

    // ── rotation ──

    [Fact]
    public void After_rotation_old_values_still_decrypt_and_new_ones_use_the_new_key()
    {
        var old = Cipher("a", ("a", MasterA));
        var stored = old.Encrypt("secret", "ctx");

        var rotated = Cipher("b", ("a", MasterA), ("b", MasterB));

        rotated.Decrypt(stored, "ctx").Should().Be("secret");
        rotated.NeedsReencryption(stored).Should().BeTrue();
        var moved = rotated.Reencrypt(stored, "ctx");
        FieldCipher.GetKeyId(moved).Should().Be("b");
        rotated.NeedsReencryption(moved).Should().BeFalse();
        rotated.Decrypt(moved, "ctx").Should().Be("secret");
    }

    [Fact]
    public void A_value_under_a_key_the_cipher_does_not_hold_is_refused()
    {
        var stored = Cipher("a", ("a", MasterA)).Encrypt("secret", "ctx");
        var act = () => Cipher("b", ("b", MasterB)).Decrypt(stored, "ctx");
        act.Should().Throw<CryptographicException>().WithMessage("*'a'*");
    }

    [Fact]
    public void The_same_master_under_two_key_ids_derives_two_keys()
    {
        var stored = Cipher("a", ("a", MasterA)).Encrypt("secret", "ctx");
        var relabelled = stored.Replace("bfc1:a:", "bfc1:z:");
        var act = () => Cipher("z", ("z", MasterA)).Decrypt(relabelled, "ctx");
        act.Should().Throw<CryptographicException>();
    }

    // ── construction ──

    [Fact]
    public void A_missing_current_key_is_refused()
    {
        var act = () => new FieldCipher("b", new Dictionary<string, byte[]> { ["a"] = MasterA });
        act.Should().Throw<ArgumentException>().WithMessage("*'b'*");
    }

    [Fact]
    public void A_short_master_is_refused()
    {
        var act = () => new FieldCipher("a", new Dictionary<string, byte[]> { ["a"] = "changeme"u8.ToArray() });
        act.Should().Throw<ArgumentException>().WithMessage("*at least 32*");
    }

    [Fact]
    public async Task A_cipher_from_a_secret_provider_reads_every_listed_key()
    {
        var secrets = new InMemorySecretProvider(new Dictionary<string, string?>
        {
            ["field-cipher-a"] = Convert.ToBase64String(MasterA),
            ["field-cipher-b"] = Convert.ToBase64String(MasterB),
        });
        var stored = Cipher("a", ("a", MasterA)).Encrypt("secret", "ctx");

        var cipher = await FieldCipher.FromSecretProviderAsync(secrets, "b", previousKeyIds: ["a"]);

        cipher.CurrentKeyId.Should().Be("b");
        cipher.Decrypt(stored, "ctx").Should().Be("secret");
    }

    [Fact]
    public async Task A_missing_master_secret_throws_at_construction_with_no_fallback()
    {
        var act = () => FieldCipher.FromSecretProviderAsync(new InMemorySecretProvider(), "a");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*field-cipher-a*not configured*");
    }

    [Fact]
    public async Task A_missing_previous_master_secret_throws_too()
    {
        var secrets = new InMemorySecretProvider(new Dictionary<string, string?> { ["field-cipher-b"] = Convert.ToBase64String(MasterB) });
        var act = () => FieldCipher.FromSecretProviderAsync(secrets, "b", previousKeyIds: ["a"]);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*field-cipher-a*");
    }

    [Fact]
    public async Task A_master_secret_that_is_not_base64_is_refused()
    {
        var secrets = new InMemorySecretProvider(new Dictionary<string, string?> { ["field-cipher-a"] = "not base64!" });
        var act = () => FieldCipher.FromSecretProviderAsync(secrets, "a");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not base64*");
    }

    [Fact]
    public void A_generated_master_secret_is_accepted()
    {
        var master = Convert.FromBase64String(FieldCipher.GenerateMasterSecret());
        master.Should().HaveCount(FieldCipher.MinimumMasterSecretBytes);
        new FieldCipher("k", new Dictionary<string, byte[]> { ["k"] = master }).CurrentKeyId.Should().Be("k");
    }

    // ── InMemorySecretProvider ──

    [Fact]
    public async Task The_in_memory_provider_is_seeded_skipping_sections_and_reads_writes_lists_and_deletes()
    {
        // The shape IConfiguration.AsEnumerable(makePathsRelative: true) yields: a section has a null value.
        var provider = new InMemorySecretProvider(new Dictionary<string, string?>
        {
            ["db"] = null,
            ["db:password"] = "p1",
            ["api-key"] = "k1",
        });

        (await provider.GetSecretAsync("db:password")).Should().Be("p1");
        (await provider.GetSecretAsync("db")).Should().BeNull();
        (await provider.ListSecretsAsync()).Should().Equal("api-key", "db:password");
        (await provider.ListSecretsAsync("db")).Should().Equal("db:password");
        provider.IsHierarchical.Should().BeFalse();

        await provider.SetSecretAsync("api-key", "k2");
        var meta = await provider.GetSecretWithMetadataAsync("api-key");
        meta!.Value.Should().Be("k2");
        meta.UpdatedAt.Should().NotBeNull();
        meta.CreatedAt.Should().BeOnOrBefore(meta.UpdatedAt!.Value);

        await provider.DeleteSecretAsync("api-key");
        (await provider.GetSecretAsync("api-key")).Should().BeNull();
        (await ((ISecretProvider)provider).GetSecretPairsAsync("db:password"))!["value"].Should().Be("p1");
    }

    [Fact]
    public async Task The_in_memory_provider_is_case_sensitive()
    {
        var provider = new InMemorySecretProvider(new Dictionary<string, string?> { ["Key"] = "v" });
        (await provider.GetSecretAsync("key")).Should().BeNull();
    }
}
