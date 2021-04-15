﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Toolkit.Uwp.Helpers;
using Windows.Storage;

namespace CommunityToolkit.Uwp.Graph.Helpers.RoamingSettings
{
    /// <summary>
    /// An IObjectStorageHelper implementation using open extensions on the Graph User for storing key/value pairs.
    /// </summary>
    public class UserExtensionDataStore : IRoamingSettingsDataStore
    {
        /// <summary>
        /// Retrieve the value from Graph User extensions and cast the response to the provided type.
        /// </summary>
        /// <typeparam name="T">The type to cast the return result to.</typeparam>
        /// <param name="userId">The id of the user.</param>
        /// <param name="extensionId">The id of the user extension.</param>
        /// <param name="key">The key for the desired value.</param>
        /// <returns>The value from the data store.</returns>
        public static async Task<T> Get<T>(string userId, string extensionId, string key)
        {
            return (T)await Get(userId, extensionId, key);
        }

        /// <summary>
        /// Retrieve the value from Graph User extensions by extensionId, userId, and key.
        /// </summary>
        /// <param name="userId">The id of the user.</param>
        /// <param name="extensionId">The id of the user extension.</param>
        /// <param name="key">The key for the desired value.</param>
        /// <returns>The value from the data store.</returns>
        public static async Task<object> Get(string userId, string extensionId, string key)
        {
            var userExtension = await GetExtensionForUser(userId, extensionId);
            return userExtension.AdditionalData[key];
        }

        /// <summary>
        /// Set a value by key in a Graph User's extension.
        /// </summary>
        /// <param name="userId">The id of the user.</param>
        /// <param name="extensionId">The id of the user extension.</param>
        /// <param name="key">The key for the target value.</param>
        /// <param name="value">The value to set.</param>
        /// <returns>A task upon completion.</returns>
        public static async Task Set(string userId, string extensionId, string key, object value)
        {
            await UserExtensionsDataSource.SetValue(userId, extensionId, key, value);
        }

        /// <summary>
        /// Creates a new roaming settings extension on a Graph User.
        /// </summary>
        /// <param name="userId">The id of the user.</param>
        /// <param name="extensionId">The id of the user extension.</param>
        /// <returns>The newly created user extension.</returns>
        public static async Task<Extension> Create(string userId, string extensionId)
        {
            var userExtension = await UserExtensionsDataSource.CreateExtension(userId, extensionId);
            return userExtension;
        }

        /// <summary>
        /// Deletes an extension by id on a Graph User.
        /// </summary>
        /// <param name="userId">The id of the user.</param>
        /// <param name="extensionId">The id of the user extension.</param>
        /// <returns>A task upon completion.</returns>
        public static async Task Delete(string userId, string extensionId)
        {
            await UserExtensionsDataSource.DeleteExtension(userId, extensionId);
        }

        /// <summary>
        /// Retrieves a user extension.
        /// </summary>
        /// <param name="userId">The id of the user.</param>
        /// <param name="extensionId">The id of the user extension.</param>
        /// <returns>The target extension.</returns>
        public static async Task<Extension> GetExtensionForUser(string userId, string extensionId)
        {
            var userExtension = await UserExtensionsDataSource.GetExtension(userId, extensionId);
            return userExtension;
        }

        private static readonly IList<string> ReservedKeys = new List<string> { "responseHeaders", "statusCode", "@odata.context" };

        /// <inheritdoc />
        public EventHandler SyncCompleted { get; set; }

        /// <inheritdoc />
        public EventHandler SyncFailed { get; set; }

        /// <inheritdoc />
        public bool AutoSync { get; }

        /// <inheritdoc />
        public string Id { get; }

        /// <inheritdoc />
        public string UserId { get; }

        /// <inheritdoc />
        public IDictionary<string, object> Cache { get; private set; }

        private readonly IObjectSerializer _serializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserExtensionDataStore"/> class.
        /// </summary>
        public UserExtensionDataStore(string userId, string extensionId, IObjectSerializer objectSerializer, bool autoSync = true)
        {
            AutoSync = autoSync;
            Id = extensionId;
            UserId = userId;
            _serializer = objectSerializer;

            Cache = null;
        }

        /// <summary>
        /// Creates a new roaming settings extension on the Graph User.
        /// </summary>
        /// <returns>The newly created Extension object.</returns>
        public async Task Create()
        {
            InitCache();

            if (AutoSync)
            {
                await Create(UserId, Id);
            }
        }

        /// <summary>
        /// Deletes the roamingSettings extension from the Graph User.
        /// </summary>
        /// <returns>A void task.</returns>
        public async Task Delete()
        {
            // Clear the cache
            Cache = null;

            if (AutoSync)
            {
                // Delete the remote.
                await Delete(UserId, Id);
            }
        }

        /// <summary>
        /// Update the remote extension to match the local cache and retrieve any new keys. Any existing remote values are replaced.
        /// </summary>
        /// <returns>The freshly synced user extension.</returns>
        public async Task Sync()
        {
            try
            {
                // Get the remote
                Extension extension = await GetExtensionForUser(UserId, Id);
                IDictionary<string, object> remoteData = extension.AdditionalData;

                if (Cache != null)
                {
                    // Send updates for all local values, overwriting the remote.
                    foreach (string key in Cache.Keys.ToList())
                    {
                        if (ReservedKeys.Contains(key))
                        {
                            continue;
                        }

                        if (!remoteData.ContainsKey(key) || !EqualityComparer<object>.Default.Equals(remoteData[key], Cache[key]))
                        {
                            Save(key, Cache[key]);
                        }
                    }
                }

                if (remoteData.Keys.Count > 0)
                {
                    InitCache();
                }

                // Update local cache with additions from remote
                foreach (string key in remoteData.Keys.ToList())
                {
                    if (!Cache.ContainsKey(key))
                    {
                        Cache.Add(key, remoteData[key]);
                    }
                }

                SyncCompleted?.Invoke(this, new EventArgs());
            }
            catch
            {
                SyncFailed?.Invoke(this, new EventArgs());
            }
        }

        /// <inheritdoc />
        public virtual bool KeyExists(string key)
        {
            return Cache != null && Cache.ContainsKey(key);
        }

        /// <inheritdoc />
        public bool KeyExists(string compositeKey, string key)
        {
            if (KeyExists(compositeKey))
            {
                ApplicationDataCompositeValue composite = (ApplicationDataCompositeValue)Cache[compositeKey];
                if (composite != null)
                {
                    return composite.ContainsKey(key);
                }
            }

            return false;
        }

        /// <inheritdoc />
        public T Read<T>(string key, T @default = default)
        {
            if (Cache != null && Cache.TryGetValue(key, out object value))
            {
                try
                {
                    return _serializer.Deserialize<T>((string)value);
                }
                catch
                {
                    // Primitive types can't be deserialized.
                    return (T)Convert.ChangeType(value, typeof(T));
                }
            }

            return @default;
        }

        /// <inheritdoc />
        public T Read<T>(string compositeKey, string key, T @default = default)
        {
            if (Cache != null)
            {
                ApplicationDataCompositeValue composite = (ApplicationDataCompositeValue)Cache[compositeKey];
                if (composite != null)
                {
                    object value = composite[key];
                    if (value != null)
                    {
                        try
                        {
                            return _serializer.Deserialize<T>((string)value);
                        }
                        catch
                        {
                            // Primitive types can't be deserialized.
                            return (T)Convert.ChangeType(value, typeof(T));
                        }
                    }
                }
            }

            return @default;
        }

        /// <inheritdoc />
        public void Save<T>(string key, T value)
        {
            InitCache();

            // Skip serialization for primitives.
            if (typeof(T) == typeof(object) || Type.GetTypeCode(typeof(T)) != TypeCode.Object)
            {
                // Update the cache
                Cache[key] = value;
            }
            else
            {
                // Update the cache
                Cache[key] = _serializer.Serialize(value);
            }

            if (AutoSync)
            {
                // Update the remote
                Task.Run(() => Set(UserId, Id, key, value));
            }
        }

        /// <inheritdoc />
        public void Save<T>(string compositeKey, IDictionary<string, T> values)
        {
            InitCache();

            if (KeyExists(compositeKey))
            {
                ApplicationDataCompositeValue composite = (ApplicationDataCompositeValue)Cache[compositeKey];

                foreach (KeyValuePair<string, T> setting in values.ToList())
                {
                    if (composite.ContainsKey(setting.Key))
                    {
                        composite[setting.Key] = _serializer.Serialize(setting.Value);
                    }
                    else
                    {
                        composite.Add(setting.Key, _serializer.Serialize(setting.Value));
                    }
                }

                // Update the cache
                Cache[compositeKey] = composite;

                if (AutoSync)
                {
                    // Update the remote
                    Task.Run(() => Set(UserId, Id, compositeKey, composite));
                }
            }
            else
            {
                ApplicationDataCompositeValue composite = new ApplicationDataCompositeValue();
                foreach (KeyValuePair<string, T> setting in values.ToList())
                {
                    composite.Add(setting.Key, _serializer.Serialize(setting.Value));
                }

                // Update the cache
                Cache[compositeKey] = composite;

                if (AutoSync)
                {
                    // Update the remote
                    Task.Run(() => Set(UserId, Id, compositeKey, composite));
                }
            }
        }

        /// <inheritdoc />
        public Task<bool> FileExistsAsync(string filePath)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public Task<T> ReadFileAsync<T>(string filePath, T @default = default)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public Task<StorageFile> SaveFileAsync<T>(string filePath, T value)
        {
            throw new NotImplementedException();
        }

        private void InitCache()
        {
            if (Cache == null)
            {
                Cache = new Dictionary<string, object>();
            }
        }
    }
}