using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Security;
using Birko.Security.Hashing;
using Birko.Security.Encryption;

namespace Birko.Framework.Examples.Security
{
    /// <summary>
    /// Examples demonstrating the Birko.Security framework:
    /// password hashing (PBKDF2), AES-256-GCM encryption, and JWT tokens.
    /// </summary>
    public static class SecurityExamples
    {
        /// <summary>
        /// PBKDF2 password hashing with SHA-512.
        /// Output format: "PBKDF2-SHA512:{iterations}:{base64salt}:{base64hash}"
        /// </summary>
        public static void RunPasswordHashingExample()
        {
            ExampleOutput.WriteLine("=== Password Hashing Example ===\n");

            // Pbkdf2PasswordHasher: 600,000 iterations by default (OWASP recommendation)
            var hasher = new Pbkdf2PasswordHasher(); // default: 600,000 iterations
            ExampleOutput.WriteLine("Created Pbkdf2PasswordHasher (600,000 iterations, SHA-512)");

            // Hash a password
            string password = "MySecureP@ssw0rd!";
            string hash = hasher.Hash(password);
            ExampleOutput.WriteLine($"\nPassword: {password}");
            ExampleOutput.WriteLine($"Hash: {hash}");
            ExampleOutput.WriteLine($"Format: PBKDF2-SHA512:{{iterations}}:{{base64salt}}:{{base64hash}}");

            // Verify correct password
            bool isValid = hasher.Verify(password, hash);
            ExampleOutput.WriteLine($"\nVerify correct password: {isValid}");

            // Verify incorrect password
            bool isInvalid = hasher.Verify("WrongPassword", hash);
            ExampleOutput.WriteLine($"Verify wrong password: {isInvalid}");

            // Each hash is unique (different random salt)
            string hash2 = hasher.Hash(password);
            ExampleOutput.WriteLine($"\nSame password, different hash: {hash != hash2}");
            ExampleOutput.WriteLine($"Hash 1: {hash[..50]}...");
            ExampleOutput.WriteLine($"Hash 2: {hash2[..50]}...");

            // Custom iteration count (minimum 10,000)
            var fastHasher = new Pbkdf2PasswordHasher(iterations: 100_000);
            ExampleOutput.WriteLine($"\nCustom hasher with 100,000 iterations (faster, less secure)");

            // IPasswordHasher interface: Hash(password) and Verify(password, hash)
            IPasswordHasher iHasher = hasher;
            ExampleOutput.WriteLine($"IPasswordHasher.Hash: returns formatted string");
            ExampleOutput.WriteLine($"IPasswordHasher.Verify: timing-safe comparison via CryptographicOperations.FixedTimeEquals");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// AES-256-GCM authenticated encryption.
        /// Output format: [12-byte nonce][16-byte tag][ciphertext]
        /// </summary>
        public static void RunEncryptionExample()
        {
            ExampleOutput.WriteLine("=== Encryption Example ===\n");

            var encryption = new AesEncryptionProvider();

            // Generate a random 256-bit key
            byte[] key = AesEncryptionProvider.GenerateKey();
            ExampleOutput.WriteLine($"Generated AES-256 key: {Convert.ToBase64String(key)}");
            ExampleOutput.WriteLine($"Key size: {key.Length} bytes (256 bits)");

            // Encrypt a string
            string plaintext = "Sensitive data: credit card 4111-1111-1111-1111";
            string encrypted = encryption.EncryptString(plaintext, key);
            ExampleOutput.WriteLine($"\nPlaintext: {plaintext}");
            ExampleOutput.WriteLine($"Encrypted (base64): {encrypted[..60]}...");

            // Decrypt the string
            string decrypted = encryption.DecryptString(encrypted, key);
            ExampleOutput.WriteLine($"Decrypted: {decrypted}");
            ExampleOutput.WriteLine($"Round-trip success: {plaintext == decrypted}");

            // Binary encrypt/decrypt
            byte[] data = System.Text.Encoding.UTF8.GetBytes("Binary data payload");
            byte[] encryptedBytes = encryption.Encrypt(data, key);
            byte[] decryptedBytes = encryption.Decrypt(encryptedBytes, key);
            string roundTrip = System.Text.Encoding.UTF8.GetString(decryptedBytes);
            ExampleOutput.WriteLine($"\nBinary round-trip: \"{roundTrip}\"");
            ExampleOutput.WriteLine($"Encrypted size: {encryptedBytes.Length} bytes (plaintext: {data.Length} + 28 overhead)");
            ExampleOutput.WriteLine($"Overhead: 12-byte nonce + 16-byte authentication tag = 28 bytes");

            // Each encryption produces different output (random nonce)
            string encrypted2 = encryption.EncryptString(plaintext, key);
            ExampleOutput.WriteLine($"\nSame plaintext, different ciphertext: {encrypted != encrypted2}");

            // Wrong key fails with CryptographicException
            byte[] wrongKey = AesEncryptionProvider.GenerateKey();
            try
            {
                encryption.DecryptString(encrypted, wrongKey);
                ExampleOutput.WriteLine("Should not reach here");
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                ExampleOutput.WriteLine("Decryption with wrong key: CryptographicException (authentication tag mismatch)");
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// JWT token generation and validation using Birko.Security.Jwt.
        /// Requires the System.IdentityModel.Tokens.Jwt NuGet package.
        /// </summary>
        public static void RunJwtExample()
        {
            ExampleOutput.WriteLine("=== JWT Token Example ===\n");

            // TokenOptions: Secret (required), Issuer, Audience, ExpirationMinutes
            var tokenOptions = new TokenOptions
            {
                Secret = "ThisIsAVeryLongSecretKeyForHmacSha256SigningAtLeast32Bytes!",
                Issuer = "BirkoFramework",
                Audience = "BirkoApp",
                ExpirationMinutes = 60,
                RefreshExpirationDays = 7
            };

            ExampleOutput.WriteLine("TokenOptions:");
            ExampleOutput.WriteLine($"  Issuer: {tokenOptions.Issuer}");
            ExampleOutput.WriteLine($"  Audience: {tokenOptions.Audience}");
            ExampleOutput.WriteLine($"  Expiration: {tokenOptions.ExpirationMinutes} minutes");
            ExampleOutput.WriteLine($"  Refresh expiration: {tokenOptions.RefreshExpirationDays} days");

            try
            {
                // JwtTokenProvider implements ITokenProvider
                var jwtProvider = new Birko.Security.Jwt.JwtTokenProvider(tokenOptions);

                // Generate a token with custom claims
                var claims = new Dictionary<string, string>
                {
                    ["sub"] = "user-123",
                    ["name"] = "John Doe",
                    ["role"] = "admin",
                    ["email"] = "john@example.com"
                };

                TokenResult tokenResult = jwtProvider.GenerateToken(claims);
                ExampleOutput.WriteLine($"\nGenerated JWT:");
                ExampleOutput.WriteLine($"  Token: {tokenResult.Token[..80]}...");
                ExampleOutput.WriteLine($"  Expires at: {tokenResult.ExpiresAt:u}");

                // Generate a refresh token (opaque random string, not JWT)
                string refreshToken = jwtProvider.GenerateRefreshToken();
                ExampleOutput.WriteLine($"  Refresh token: {refreshToken}");

                // Validate the token
                TokenValidationResult validation = jwtProvider.ValidateToken(tokenResult.Token);
                ExampleOutput.WriteLine($"\nToken validation:");
                ExampleOutput.WriteLine($"  IsValid: {validation.IsValid}");
                if (validation.IsValid)
                {
                    ExampleOutput.WriteLine($"  Claims:");
                    foreach (var claim in validation.Claims)
                    {
                        ExampleOutput.WriteLine($"    {claim.Key} = {claim.Value}");
                    }
                }

                // Validate with wrong secret
                var wrongOptions = new TokenOptions
                {
                    Secret = "ADifferentSecretKeyThatDoesNotMatchTheOriginalOne!!",
                    Issuer = tokenOptions.Issuer,
                    Audience = tokenOptions.Audience
                };
                TokenValidationResult invalidResult = jwtProvider.ValidateToken(tokenResult.Token, wrongOptions);
                ExampleOutput.WriteLine($"\nValidation with wrong secret:");
                ExampleOutput.WriteLine($"  IsValid: {invalidResult.IsValid}");
                ExampleOutput.WriteLine($"  Error: {invalidResult.Error}");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"\nJWT operations require System.IdentityModel.Tokens.Jwt package: {ex.Message}");
            }

            ExampleOutput.WriteLine("\nITokenProvider interface:");
            ExampleOutput.WriteLine("  GenerateToken(claims, options?)   - create signed JWT");
            ExampleOutput.WriteLine("  GenerateRefreshToken()            - random opaque token");
            ExampleOutput.WriteLine("  ValidateToken(token, options?)    - verify and extract claims");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }
    }
}
