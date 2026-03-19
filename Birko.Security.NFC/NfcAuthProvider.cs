using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.NFC
{
    /// <summary>
    /// Default NFC authentication provider using an in-memory tag mapping store.
    /// For production use, supply an <see cref="INfcTagMappingStore"/> backed by a database.
    ///
    /// Optionally integrates with <see cref="ITokenProvider"/> to issue JWTs on successful authentication.
    /// </summary>
    public class NfcAuthProvider : INfcAuthProvider
    {
        private readonly INfcTagMappingStore _store;
        private readonly ITokenProvider? _tokenProvider;
        private readonly TokenOptions? _tokenOptions;
        private readonly NfcAuthSettings _settings;

        /// <summary>
        /// Creates an NFC auth provider.
        /// </summary>
        /// <param name="store">Tag mapping store (in-memory or database-backed).</param>
        /// <param name="settings">Auth settings.</param>
        /// <param name="tokenProvider">Optional token provider for issuing JWTs.</param>
        /// <param name="tokenOptions">Optional token options.</param>
        public NfcAuthProvider(
            INfcTagMappingStore store,
            NfcAuthSettings? settings = null,
            ITokenProvider? tokenProvider = null,
            TokenOptions? tokenOptions = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _settings = settings ?? new NfcAuthSettings();
            _tokenProvider = tokenProvider;
            _tokenOptions = tokenOptions;
        }

        public async Task<NfcAuthResult> AuthenticateAsync(string tagUid, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tagUid))
            {
                return NfcAuthResult.Failure(tagUid ?? "", "Tag UID cannot be empty.");
            }

            var normalizedUid = _settings.NormalizeUids ? NormalizeUid(tagUid) : tagUid;
            var mapping = await _store.GetByTagUidAsync(normalizedUid, cancellationToken).ConfigureAwait(false);

            if (mapping == null || !mapping.IsActive)
            {
                return NfcAuthResult.Failure(normalizedUid, "Tag is not registered or has been revoked.");
            }

            if (_settings.EnforceExpiration && mapping.IsExpired)
            {
                return NfcAuthResult.Failure(normalizedUid, "Tag registration has expired.");
            }

            // Update last used timestamp
            if (_settings.TrackUsage)
            {
                mapping.LastUsedAt = DateTime.UtcNow;
                await _store.UpdateAsync(mapping, cancellationToken).ConfigureAwait(false);
            }

            // Issue token if configured
            TokenResult? token = null;
            if (_settings.IssueTokens && _tokenProvider != null)
            {
                var claims = new Dictionary<string, string>
                {
                    ["sub"] = mapping.UserId.ToString(),
                    ["nfc_uid"] = normalizedUid,
                    ["auth_method"] = "nfc"
                };

                if (!string.IsNullOrEmpty(mapping.Email))
                {
                    claims["email"] = mapping.Email;
                }
                if (!string.IsNullOrEmpty(mapping.UserName))
                {
                    claims["name"] = mapping.UserName;
                }

                token = _tokenProvider.GenerateToken(claims, _tokenOptions);
            }

            return NfcAuthResult.Success(mapping.UserId, normalizedUid, token, mapping.UserName, mapping.Email);
        }

        public async Task<NfcTagMapping> EnrollAsync(Guid userId, string tagUid, string? label = null, string? userName = null, string? email = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tagUid))
            {
                throw new ArgumentException("Tag UID cannot be empty.", nameof(tagUid));
            }

            var normalizedUid = _settings.NormalizeUids ? NormalizeUid(tagUid) : tagUid;

            // Check if already enrolled
            var existing = await _store.GetByTagUidAsync(normalizedUid, cancellationToken).ConfigureAwait(false);
            if (existing != null && existing.IsActive)
            {
                throw new InvalidOperationException($"Tag {normalizedUid} is already enrolled for user {existing.UserId}.");
            }

            // Check max tags per user
            if (_settings.MaxTagsPerUser > 0)
            {
                var userTags = await _store.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
                var activeTags = userTags.Count(t => t.IsActive);
                if (activeTags >= _settings.MaxTagsPerUser)
                {
                    throw new InvalidOperationException($"User {userId} already has {activeTags} active tags (max: {_settings.MaxTagsPerUser}).");
                }
            }

            var mapping = new NfcTagMapping
            {
                TagUid = normalizedUid,
                UserId = userId,
                UserName = userName,
                Email = email,
                Label = label,
                IsActive = true,
                EnrolledAt = DateTime.UtcNow
            };

            await _store.AddAsync(mapping, cancellationToken).ConfigureAwait(false);
            return mapping;
        }

        public async Task RevokeAsync(string tagUid, CancellationToken cancellationToken = default)
        {
            var normalizedUid = _settings.NormalizeUids ? NormalizeUid(tagUid) : tagUid;
            var mapping = await _store.GetByTagUidAsync(normalizedUid, cancellationToken).ConfigureAwait(false);
            if (mapping != null)
            {
                mapping.IsActive = false;
                await _store.UpdateAsync(mapping, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var tags = await _store.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
            foreach (var tag in tags.Where(t => t.IsActive))
            {
                tag.IsActive = false;
                await _store.UpdateAsync(tag, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<NfcTagMapping>> GetUserTagsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var tags = await _store.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
            return tags.Where(t => t.IsActive).ToList();
        }

        public async Task<NfcTagMapping?> GetTagMappingAsync(string tagUid, CancellationToken cancellationToken = default)
        {
            var normalizedUid = _settings.NormalizeUids ? NormalizeUid(tagUid) : tagUid;
            return await _store.GetByTagUidAsync(normalizedUid, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> IsEnrolledAsync(string tagUid, CancellationToken cancellationToken = default)
        {
            var normalizedUid = _settings.NormalizeUids ? NormalizeUid(tagUid) : tagUid;
            var mapping = await _store.GetByTagUidAsync(normalizedUid, cancellationToken).ConfigureAwait(false);
            return mapping != null && mapping.IsActive;
        }

        private static string NormalizeUid(string uid)
        {
            return uid.Replace(":", "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
        }
    }

    /// <summary>
    /// Persistence interface for NFC tag-to-user mappings.
    /// Implement this with any Birko.Data store for production use.
    /// </summary>
    public interface INfcTagMappingStore
    {
        Task<NfcTagMapping?> GetByTagUidAsync(string tagUid, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<NfcTagMapping>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task AddAsync(NfcTagMapping mapping, CancellationToken cancellationToken = default);
        Task UpdateAsync(NfcTagMapping mapping, CancellationToken cancellationToken = default);
        Task DeleteAsync(string tagUid, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// In-memory implementation of <see cref="INfcTagMappingStore"/> for testing and development.
    /// </summary>
    public class InMemoryNfcTagMappingStore : INfcTagMappingStore
    {
        private readonly ConcurrentDictionary<string, NfcTagMapping> _byUid = new();

        public Task<NfcTagMapping?> GetByTagUidAsync(string tagUid, CancellationToken cancellationToken = default)
        {
            _byUid.TryGetValue(tagUid, out var mapping);
            return Task.FromResult(mapping);
        }

        public Task<IReadOnlyList<NfcTagMapping>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var result = _byUid.Values.Where(m => m.UserId == userId).ToList();
            return Task.FromResult<IReadOnlyList<NfcTagMapping>>(result);
        }

        public Task AddAsync(NfcTagMapping mapping, CancellationToken cancellationToken = default)
        {
            if (!_byUid.TryAdd(mapping.TagUid, mapping))
            {
                throw new InvalidOperationException($"Tag {mapping.TagUid} already exists in the store.");
            }
            return Task.CompletedTask;
        }

        public Task UpdateAsync(NfcTagMapping mapping, CancellationToken cancellationToken = default)
        {
            _byUid[mapping.TagUid] = mapping;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string tagUid, CancellationToken cancellationToken = default)
        {
            _byUid.TryRemove(tagUid, out _);
            return Task.CompletedTask;
        }
    }
}
