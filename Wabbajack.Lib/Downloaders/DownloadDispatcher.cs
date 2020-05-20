﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Alphaleonis.Win32.Filesystem;
using Wabbajack.Common;
using Wabbajack.Lib.Downloaders.UrlDownloaders;

namespace Wabbajack.Lib.Downloaders
{
    public static class DownloadDispatcher
    {
        public static readonly List<IDownloader> Downloaders = new List<IDownloader>()
        {
            new GameFileSourceDownloader(),
            new MegaDownloader(),
            new DropboxDownloader(),
            new GoogleDriveDownloader(),
            new ModDBDownloader(),
            new NexusDownloader(),
            new MediaFireDownloader(),
            new LoversLabDownloader(),
            new VectorPlexusDownloader(),
            new DeadlyStreamDownloader(),
            new BethesdaNetDownloader(),
            new TESAllianceDownloader(),
            new YouTubeDownloader(),
            new WabbajackCDNDownloader(),
            new HTTPDownloader(),
            new ManualDownloader(),
        };

        public static readonly List<IUrlInferencer> Inferencers = new List<IUrlInferencer>()
        {
            new BethesdaNetInferencer(),
            new YoutubeInferencer(),
            new WabbajackCDNInfluencer()
        };

        private static readonly Dictionary<Type, IDownloader> IndexedDownloaders;

        static DownloadDispatcher()
        {
            IndexedDownloaders = Downloaders.ToDictionary(d => d.GetType());
        }

        public static async Task<AbstractDownloadState?> Infer(Uri uri)
        {
            foreach (var inf in Inferencers)
            {
                var state = await inf.Infer(uri);
                if (state != null)
                    return state;
            }
            return null;

        }

        public static T GetInstance<T>() where T : IDownloader
        {
            var inst = (T)IndexedDownloaders[typeof(T)];
            return inst;
        }

        public static async Task<AbstractDownloadState> ResolveArchive(dynamic ini, bool quickMode = false)
        {
            var states = await Task.WhenAll(Downloaders.Select(d => (Task<AbstractDownloadState>)d.GetDownloaderState(ini, quickMode)));
            return states.FirstOrDefault(result => result != null);
        }

        /// <summary>
        /// Reduced version of Resolve archive that requires less information, but only works
        /// with a single URL string
        /// </summary>
        /// <param name="ini"></param>
        /// <returns></returns>
        public static AbstractDownloadState? ResolveArchive(string url)
        {
            return Downloaders.OfType<IUrlDownloader>().Select(d => d.GetDownloaderState(url)).FirstOrDefault(result => result != null);
        }

        public static async Task PrepareAll(IEnumerable<AbstractDownloadState> states)
        {
            await Task.WhenAll(states.Select(s => s.GetDownloader().GetType())
                  .Distinct()
                  .Select(t => Downloaders.First(d => d.GetType() == t).Prepare()));
        }

        public static async Task<bool> DownloadWithPossibleUpgrade(Archive archive, AbsolutePath destination)
        {
            var success = await Download(archive, destination);
            if (success)
            {
                await destination.FileHashCachedAsync();
                return true;
            }

            if (!(archive.State is IUpgradingState))
            {
                Utils.Log($"Download failed for {archive.Name} and no upgrade from this download source is possible");
                return false;
            }

            var upgrade = (IUpgradingState)archive.State;

            Utils.Log($"Trying to find solution to broken download for {archive.Name}");
            
            var result = await upgrade.FindUpgrade(archive);
            if (result == default)
            {
                Utils.Log(
                    $"No solution for broken download {archive.Name} {archive.State.PrimaryKeyString} could be found");
                return false;

            }

            Utils.Log($"Looking for patch for {archive.Name}");
            var patchResult = await ClientAPI.GetModUpgrade(archive, result.Archive!);

            Utils.Log($"Downloading patch for {archive.Name}");
            
            var tempFile = new TempFile();

            using var response = await (new Common.Http.Client()).GetAsync(patchResult);
            await tempFile.Path.WriteAllAsync(await response.Content.ReadAsStreamAsync());
            response.Dispose();

            Utils.Log($"Applying patch to {archive.Name}");
            await using(var src = result.NewFile.Path.OpenShared())
            await using (var final = destination.Create())
            {
                Utils.ApplyPatch(src, () => tempFile.Path.OpenShared(), final);
            }

            var hash = await destination.FileHashCachedAsync();
            if (hash != archive.Hash && archive.Hash != default)
            {
                Utils.Log("Archive hash didn't match after patching");
                return false;
            }

            return true;
        }

        private static async Task<bool> Download(Archive archive, AbsolutePath destination)
        {
            try
            {
                var result =  await archive.State.Download(archive, destination);
                if (!result) return false;

                if (!archive.Hash.IsValid) return true;
                var hash = await destination.FileHashCachedAsync();
                if (hash == archive.Hash) return true;

                Utils.Log($"Hashed download is incorrect");
                return false;

            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
