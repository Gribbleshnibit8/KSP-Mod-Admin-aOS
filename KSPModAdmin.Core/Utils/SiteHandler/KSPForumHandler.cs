using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using HtmlAgilityPack;
using KSPModAdmin.Core.Controller;
using KSPModAdmin.Core.Model;
using KSPModAdmin.Core.Views;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace KSPModAdmin.Core.Utils.SiteHandler
{
	public class KspForumHandler : ISiteHandler
	{
		private const string cName = "KSP Forum";
		private const string Host = "forum.kerbalspaceprogram.com";
		private HtmlDocument HtmlDoc;

		/// <summary>
		/// Gets the Name of the ISiteHandler.
		/// </summary>
		/// <returns>The Name of the ISiteHandler.</returns>
		public string Name { get { return cName; } }

		/// <summary>
		/// Checks if the passed URL is a KSP Forum URL.
		/// </summary>
		/// <param name="url">The URL to check.</param>
		/// <returns>True if the passed URL is a valid KSP Forum URL, otherwise false.</returns>
		public bool IsValidURL(string url)
		{
			return (!string.IsNullOrEmpty(url) && Host.Equals(new Uri(url).Authority) && url.Contains("threads"));
		}

		/// <summary>
		/// Gets the content of the site of the passed URL and parses it for ModInfos.
		/// </summary>
		/// <param name="url">The URL of the site to parse the ModInfos from.</param>
		/// <returns>The ModInfos parsed from the site of the passed URL.</returns>
		public ModInfo GetModInfo(string url)
		{
			HtmlDoc = new HtmlWeb().Load(url);
			HtmlDoc.OptionFixNestedTags = true;
			var modInfo = new ModInfo
			{
				SiteHandlerName = Name,
				ModURL = ReduceToPlainUrl(url)
			};
			if (ParseSite(url, ref modInfo))
				return modInfo;
			return null;
		}

		/// <summary>
		/// Handles a mod add via URL.
		/// Validates the URL, gets ModInfos, downloads mod archive, adds it to the ModSelection and installs the mod if selected.
		/// </summary>
		/// <param name="url">The URL to the mod.</param>
		/// <param name="modName">The name for the mod.</param>
		/// <param name="install">Flag to determine if the mod should be installed after adding.</param>
		/// <param name="downloadProgressHandler">Callback function for download progress.</param>
		/// <return>The root node of the added mod, or null.</return>
		public ModNode HandleAdd(string url, string modName, bool install, DownloadProgressChangedEventHandler downloadProgressHandler = null)
		{
			ModInfo modInfo = GetModInfo(url);
			if (modInfo == null)
				return null;

			if (!string.IsNullOrEmpty(modName))
				modInfo.Name = modName;

			ModNode newMod = null;
			if (DownloadMod(ref modInfo, downloadProgressHandler))
				newMod = ModSelectionController.HandleModAddViaModInfo(modInfo, install);

			return newMod;
		}

        /// <summary>
        /// Checks if updates are available for the passed mod.
        /// </summary>
        /// <param name="modInfo">The ModInfos of the mod to check for updates.</param>
        /// <param name="newModInfo">A reference to an empty ModInfo to write the updated ModInfos to.</param>
        /// <returns>True if there is an update, otherwise false.</returns>
        public bool CheckForUpdates(ModInfo modInfo, ref ModInfo newModInfo)
        {
            newModInfo = GetModInfo(modInfo.ModURL);
	        return !modInfo.Version.Equals(newModInfo.Version);
        }

        /// <summary>
        /// Downloads the mod.
        /// </summary>
        /// <param name="modInfo">The infos of the mod. Must have at least ModURL and LocalPath</param>
        /// <param name="downloadProgressHandler">Callback function for download progress.</param>
        /// <returns>True if the mod was downloaded.</returns>
        public bool DownloadMod(ref ModInfo modInfo, DownloadProgressChangedEventHandler downloadProgressHandler = null)
        {
            if (modInfo == null)
                return false;

			var downloadInfos = GetDownloadInfos();
			DownloadInfo selected = null;

			if (downloadInfos.Count > 1)
			{
				// create new selection form if more than one download option found
				var dlg = new frmSelectDownload(downloadInfos);
				if (dlg.ShowDialog() != DialogResult.OK)
					return false;

				selected = dlg.SelectedLink;
				dlg.InvalidateView();
			}
			else
			{
				selected = downloadInfos.First();
			}

            string downloadUrl = GetDownloadUrl(modInfo);
			modInfo.LocalPath = Path.Combine(OptionsController.DownloadPath, GetDownloadName(downloadUrl));
            www.DownloadFile(downloadUrl, modInfo.LocalPath, downloadProgressHandler);

            return File.Exists(modInfo.LocalPath);
        }

	    /// <summary>
	    /// Returns the plain url to the mod, where the ModInfos would be get from.
	    /// </summary>
	    /// <param name="url">The url to reduce.</param>
	    /// <returns>The plain url to the mod, where the ModInfos would be get from.</returns>
	    public string ReduceToPlainUrl(string url)
	    {
			return url.Substring(0, url.IndexOf('-'));
	    }

		private bool ParseSite(string url, ref ModInfo modInfo)
		{
			// To scrape the fields, now using HtmlAgilityPack and XPATH search strings.
			// Easy way to get XPATH search: use chrome, inspect element, highlight the needed data and right-click and copy XPATH

			// gets name, version, and ID
			HtmlNode nameNode = HtmlDoc.DocumentNode.SelectSingleNode("//*[@id='pagetitle']/h1/span/a");
			HtmlNode authorNode = HtmlDoc.DocumentNode.SelectSingleNode("//*[@id='posts']/li[1]/div[2]/div[1]/div[1]/div[1]/a");
			HtmlNode createNode = HtmlDoc.DocumentNode.SelectSingleNode("//*[@id='posts']/li[1]/div[1]/span[1]");
			HtmlNode updateNode = HtmlDoc.DocumentNode.SelectSingleNode("//*[@id='posts']/li[1]/div[2]/div[2]/div[2]/blockquote[1]");

			//*[@id='posts']/li[1]/div[2]/div[2]/div[1]/div

			if (nameNode == null)
				return false;

			modInfo.Name = nameNode.InnerHtml;
			modInfo.ProductID = new Regex(@".*\/(.*?)-.*").Replace(nameNode.Attributes["href"].Value, "$1");
			modInfo.KSPVersion = new Regex(@"\[(.*)\].*").Replace(nameNode.InnerHtml, "$1");
			modInfo.Author = authorNode.InnerText.Trim();

			modInfo.CreationDateAsDateTime = GetDateTime(createNode.InnerText.Trim());
			modInfo.ChangeDateAsDateTime =  GetDateTime(updateNode.InnerText.Trim());

			return true;

			// more infos could be parsed here (like: short description, Tab content (overview, installation, ...), comments, ...)
		}

		/// <summary>
		/// Converts a date string into a DateTime object
		/// </summary>
		/// <param name="dateString">The string of text to convert</param>
		/// <returns>DateTime object</returns>
	    private DateTime GetDateTime(string dateString)
        {
			dateString = HtmlEntity.DeEntitize(dateString);

			DateTime date;

			// Standard creation date and edit date longer than 2 days
			if (DateTime.TryParse(new Regex(@"(st|rd|th|nd|at)").Replace(dateString, ""), out date))
				return date;

			// Prepare an edited date for parsing
			dateString = dateString.Substring(dateString.IndexOf(';') + 1);
			// TODO if forums use localization these strings need localization
			if (dateString.Contains("Today"))
			{
				date = DateTime.Now;
				dateString = dateString.Replace("Today at", "").Trim();
				var time = Convert.ToDateTime(dateString);
				return new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second);
			}

			if (dateString.Contains("Yesterday"))
			{
				date = DateTime.Now.AddDays(-1);
				dateString = dateString.Replace("Yesterday at", "").Trim();
				var time = Convert.ToDateTime(dateString);
				return new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second);
			}

			// If all else fails just make the date today
			return DateTime.Now;
        }

        private string GetDownloadUrl(ModInfo modInfo)
        {
	        string url;
	        if (!modInfo.ModURL.Contains("releases"))
	        {
				var parts = GetUrlParts(modInfo.ModURL);
				url = parts[0] + "://" + parts[1] + "/" + parts[2] + "/" + parts[3] + "/releases";
	        }
	        else
	        {
		        url = modInfo.ModURL;
	        }

			return url;
        }

		private string GetDownloadName(string url)
		{
			return new Uri(url).Segments.Last();
		}

		/// <summary>
		/// Splits a url into it's segment parts
		/// </summary>
		/// <param name="url">A url to split</param>
		/// <exception cref="ArgumentException"></exception>
		/// <returns>An array of the url segments</returns>
		private List<string> GetUrlParts(string url)
		{
			List<string> parts = null;

			//for (int index = 0; index < parts.Count; index++)
			//{
			//	parts[index] = parts[index].Trim(new char[] { '/' });
			//}

			

			//// Remove empty parts from the list
			//parts = parts.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();

			//// TODO Error message should go wherever strings are going.
			//if (parts.Count < 4)
			//	throw new System.ArgumentException("Forum URL must point to a thread.");

			return parts;
		}

		private List<DownloadInfo> GetDownloadInfos()
		{
			var links = new List<DownloadInfo>();
			foreach (var link in HtmlDoc.DocumentNode.SelectNodes("//*[@id='posts']/li[1]/div[2]/div[2]/div/div/div/blockquote//a"))
			{
				var uri = link.Attributes["href"].Value;
				if (Uri.IsWellFormedUriString(uri, UriKind.Absolute))
				{
					var siteHandler = SiteHandlerManager.GetSiteHandlerByURL(uri);
					var dInfo = new DownloadInfo { Name = link.InnerText };

					// If there's a handler for this we know it will be supported, get as much information as possible
					if (siteHandler != null)
					{
						dInfo.KnownHost = true;
						if (dInfo.Name == null || dInfo.Name.Contains("://"))
							dInfo.Name = siteHandler.Name;
					}

					// Get the URL
					dInfo.DownloadURL = uri;

					links.Add(dInfo);

				}
			}
			return links;
		}
    }
}
