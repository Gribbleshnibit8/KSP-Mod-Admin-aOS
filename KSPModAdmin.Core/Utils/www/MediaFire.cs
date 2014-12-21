using System;
using System.Windows.Forms.VisualStyles;
using HtmlAgilityPack;

namespace KSPModAdmin.Core.Utils
{
    public abstract class MediaFire
    {
        public static bool IsValidURL(string url)
        {
			return (new Uri(url).Authority.Equals("www.mediafire.com"));
        }

        public static string GetDownloadURL(string mediafireURL)
        {
            if (string.IsNullOrEmpty(mediafireURL))
                return string.Empty;

	        // Load the page
			HtmlDocument htmlDoc = new HtmlWeb().Load(mediafireURL);
			htmlDoc.OptionFixNestedTags = true;
			HtmlNode downloadNode = htmlDoc.DocumentNode.SelectSingleNode("//*[@class='dl-utility-nav']/ul/li[3]/a");

	        return downloadNode.Attributes["href"].Value;
        }

        public static string GetFileName(string downloadUrl)
        {
            int index = downloadUrl.LastIndexOf("/");
            string filename = downloadUrl.Substring(index + 1);
            if (filename.Contains("?"))
                filename = filename.Substring(0, filename.IndexOf("?"));

            return filename;
        }
    }
}
