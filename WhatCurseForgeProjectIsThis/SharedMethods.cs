﻿using CurseForge.APIClient;
using CurseForge.APIClient.Models;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace WhatCurseForgeProjectIsThis
{
    public static class SharedMethods
    {
        public static async Task<List<Game>> GetGameInfo(IDatabaseAsync _redis, ApiClient _cfApiClient)
        {
            var cachedGames = await _redis.StringGetAsync("cf-games");

            if (!cachedGames.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<List<Game>>(cachedGames);
            }

            var games = await _cfApiClient.GetGamesAsync();
            await _redis.StringSetAsync("cf-games", JsonConvert.SerializeObject(games.Data), TimeSpan.FromMinutes(5));
            return games.Data;
        }

        public static async Task<List<Category>> GetCategoryInfo(IDatabaseAsync _redis, ApiClient _cfApiClient, List<Game> gameInfo, string game)
        {
            var cachedCategories = await _redis.StringGetAsync($"cf-categories-{game}");

            if (!cachedCategories.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<List<Category>>(cachedCategories);
            }

            var gameId = gameInfo.FirstOrDefault(x => x.Slug.Equals(game, StringComparison.InvariantCultureIgnoreCase))?.Id;

            var categories = await _cfApiClient.GetCategoriesAsync(gameId);
            await _redis.StringSetAsync($"cf-categories-{game}", JsonConvert.SerializeObject(categories.Data), TimeSpan.FromMinutes(5));

            return categories.Data;
        }

        public static async Task<List<Category>> GetCategoryInfo(IDatabaseAsync _redis, ApiClient _cfApiClient, int gameId)
        {
            var cachedCategories = await _redis.StringGetAsync($"cf-categories-id-{gameId}");

            if (!cachedCategories.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<List<Category>>(cachedCategories);
            }

            var categories = await _cfApiClient.GetCategoriesAsync(gameId);
            await _redis.StringSetAsync($"cf-categories-id-{gameId}", JsonConvert.SerializeObject(categories.Data), TimeSpan.FromMinutes(5));
            await Task.Delay(25);
            return categories.Data;
        }

        public static async Task<Mod?> SearchModAsync(IDatabaseAsync _redis, ApiClient _cfApiClient, int projectId)
        {
            var modResultCache = await _redis.StringGetAsync($"cf-mod-{projectId}");
            if (!modResultCache.IsNull)
            {
                if (modResultCache == "empty")
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<Mod>(modResultCache); ;
            }

            try
            {
                var modResult = await _cfApiClient.GetModAsync(projectId);

                if (modResult.Data.Name == $"project-{projectId}" && modResult.Data.LatestFiles.Count > 0)
                {
                    var projectName = GetProjectNameFromFile(modResult.Data.LatestFiles.OrderByDescending(m => m.FileDate).First().DownloadUrl);
                    // Replace the project name with the filename for the projects latest available file
                    modResult.Data.Name = projectName;
                }

                var modJson = JsonConvert.SerializeObject(modResult.Data);

                await _redis.StringSetAsync($"cf-mod-{projectId}", modJson, TimeSpan.FromMinutes(5));

                return modResult.Data;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<int?> SearchModFileAsync(IDatabaseAsync _redis, ApiClient _cfApiClient, long fileId)
        {
            var modResultCache = await _redis.StringGetAsync($"cf-file-{fileId}");
            if (!modResultCache.IsNull)
            {
                if (modResultCache == "empty")
                {
                    return null;
                }

                var obj = JsonConvert.DeserializeObject<GenericListResponse<CurseForge.APIClient.Models.Files.File>>(modResultCache);

                if (obj?.Data.Count > 0)
                {
                    return obj.Data[0].ModId;
                }
            }

            try
            {
                var modResult = await _cfApiClient.GetFilesAsync(new CurseForge.APIClient.Models.Files.GetModFilesRequestBody
                {
                    FileIds = new List<long> { fileId }
                });

                if (modResult.Data.Count > 0)
                {
                    var modJson = JsonConvert.SerializeObject(modResult);
                    await _redis.StringSetAsync($"cf-file-{fileId}", modJson, TimeSpan.FromMinutes(5));

                    return modResult.Data[0].ModId;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<Mod?> SearchForSlug(IDatabaseAsync _redis, ApiClient _cfApiClient, List<Game> gameInfo, List<Category> categoryInfo, string game, string category, string slug)
        {
            var cachedResponse = await _redis.StringGetAsync($"cf-mod-{game}-{category}-{slug}");
            if (!cachedResponse.IsNullOrEmpty)
            {
                if (cachedResponse == "empty")
                {
                    return null;
                }

                var cachedMod = JsonConvert.DeserializeObject<Mod>(cachedResponse);

                return cachedMod;
            }

            var gameId = gameInfo.FirstOrDefault(x => x.Slug.Equals(game, StringComparison.InvariantCultureIgnoreCase))?.Id;
            var categoryId = categoryInfo.FirstOrDefault(x => x.Slug.Equals(category, StringComparison.InvariantCultureIgnoreCase))?.Id;

            if (!gameId.HasValue || !categoryId.HasValue)
            {
                return null;
            }

            var mod = await _cfApiClient.SearchModsAsync(gameId.Value, classId: categoryId, slug: slug);

            if (mod.Data.Count == 0)
            {
                mod = await _cfApiClient.SearchModsAsync(gameId.Value, categoryId: categoryId, slug: slug);
            }

            if (mod.Data.Count == 1)
            {
                await _redis.StringSetAsync($"cf-mod-{game}-{category}-{slug}", JsonConvert.SerializeObject(mod.Data[0]), TimeSpan.FromMinutes(5));
                return mod.Data[0];
            }

            return null;
        }

        public static string GetProjectNameFromFile(string url)
        {
            return Path.GetFileName(url);
        }
    }
}