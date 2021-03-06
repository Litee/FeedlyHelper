﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using RestSharp;
using System.Net;

namespace FeedlyHelper
{
    public class Program
    {
        private const string FeedlyHelperIniFilePath = "./FeedlyHelper.ini";

        public static void Main(string[] args)
        {
            Console.Out.WriteLine("Starting FeedlyHelper v{0} ...", Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
            var configLines = File.Exists(FeedlyHelperIniFilePath) ? File.ReadAllLines(FeedlyHelperIniFilePath) : new string[0];
            var userId = configLines.FirstOrDefault(line => line.StartsWith("userId="));
            var authToken = configLines.FirstOrDefault(line => line.StartsWith("authToken="));
            userId = userId != null && userId.Length > 7 ? userId.Substring(7) : "";
            authToken = authToken != null && authToken.Length > 10 ? authToken.Substring(10) : "";
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(authToken))
            {
                File.WriteAllText(FeedlyHelperIniFilePath, "userId=<PUT USER ID HERE>\r\nauthToken=<PUT AUTH TOKEN HERE>");
                Console.Out.WriteLine("ERROR: No user ID or auth token defined - please get from https://developer.feedly.com/v3/developer/ and define in file {0}", FeedlyHelperIniFilePath);
                Environment.Exit(-1);
            }
            var verb = args.FirstOrDefault();
            if (verb == "mark-as-read")
            {
                string category = null;
                var minimalEngagement = -1;
                var auto = false;
                var minAge = 0;
                var removeDuplicates = false;
                var blacklistedWords = "";
                var queue = new Queue<string>(args);
                try
                {
                    while (queue.Any())
                    {
                        var queueItem = queue.Dequeue();
                        if (queueItem == "--category")
                        {
                            category = queue.Dequeue();
                        }
                        if (queueItem == "--engagement-less-than")
                        {
                            minimalEngagement = Convert.ToInt32(queue.Dequeue());
                        }
                        if (queueItem == "--no-confirmation")
                        {
                            auto = true;
                        }
                        if (queueItem == "--min-entry-age-days")
                        {
                            minAge = Convert.ToInt32(queue.Dequeue());
                        }
                        if (queueItem == "--remove-duplicates")
                        {
                            removeDuplicates = true;
                        }
                        if (queueItem == "--blacklisted-words")
                        {
                            blacklistedWords = queue.Dequeue();
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR: Could not parse command line parameters! Details:");
                    throw;
                }
                Console.Out.WriteLine("---------------------------------");
                Console.Out.WriteLine("Executing Mark-As-Read command with parameters:");
                Console.Out.WriteLine("Category: '{0}'", category);
                Console.Out.WriteLine("Minimal engagement level: {0}", minimalEngagement > 0 ? minimalEngagement.ToString() : "None");
                Console.Out.WriteLine("Minimal entry age: {0}", minAge);
                Console.Out.WriteLine("Mark as read automatically: {0}", auto);
                Console.Out.WriteLine("---------------------------------");

                var client1 = new RestClient { BaseUrl = new Uri("https://cloud.feedly.com/v3/") };
                //var unreadItItems1 = GetStreamItems(client1, "A - Programming").Concat(GetStreamItems(client1, "Engineering Blogs")).Concat(GetStreamItems(client1, "A - IT Many News"));
                //                var unreadNewsItems1 = GetStreamItems(client1, "A - News");
                try
                {
                    var unreadNewsItems1 = GetStreamItems(client1, category, userId, authToken);
                    //var stopWords1 = new[] { "icymi", "youtrack", "wordpress", "yii", "php", "ruby", "objective-c", "clojure", "kotlin", "laravel", "watchos", "zfs", "rocksdb", "xcode", "ionic", " rails", "gcc", "collective #", "sponsored post" };
                    Console.Out.WriteLine("Selecting items to mark as read...");
                    var lowEngagement = unreadNewsItems1.Where(item => item.Engagement < minimalEngagement && item.CrawledDate.CompareTo(DateTime.UtcNow.AddDays(-minAge)) < 0).OrderBy(item => item.CrawledDate).Select(item => new { FeedlyEntry = item, Reason = "Engagement < " + minimalEngagement }).ToArray();
                    var toMarkAsRead = lowEngagement;
                    if (removeDuplicates)
                    {
                        var duplicates = unreadNewsItems1.Where(x => x.Title != null).Except(lowEngagement.Select(x => x.FeedlyEntry)).GroupBy(item => item.Title).Where(items => items.Count() > 1).Select(items => new { FeedlyEntry = items.OrderBy(x => x.Engagement).First(), Reason = "Duplicates: " + string.Join("; ", items.Select(item => "[" + item.Engagement + "]")) }).ToArray();
                        toMarkAsRead = toMarkAsRead.Concat(duplicates).ToArray();
                    }
                    if (!string.IsNullOrEmpty(blacklistedWords))
                    {
                        var blacklistedWordsAsList = blacklistedWords.Split(';').Select(x => x.ToLower().Trim()).Where(x => x.Length > 0);
                        var blacklisted = unreadNewsItems1.Where(item => item?.Title != null)
                            .Where(item => blacklistedWordsAsList.Any(sw => item.Title.ToLower().Contains(sw.ToLower())))
                        .Select(item =>
                        {
                            var matchedWords = string.Join(";", blacklistedWordsAsList.Where(sw => item.Title.ToLower().Contains(sw.ToLower())));
                            return new {FeedlyEntry = item, Reason = "Blacklisted word in title: " + matchedWords};
                        })
                        .ToArray();
                        toMarkAsRead = toMarkAsRead.Concat(blacklisted).ToArray();
                    }
                    if (toMarkAsRead.Length > 0)
                    {
                        foreach (var actionItem in toMarkAsRead)
                        {
                            Console.Out.WriteLine("Want to mark as read: " + actionItem.FeedlyEntry.CrawledDate + " " + actionItem.FeedlyEntry.Title.Replace("\n", "") + " [" + actionItem.FeedlyEntry.Engagement + "]. Reason: " + actionItem.Reason);
                        }
                        bool approved;
                        if (auto)
                        {
                            approved = true;
                        }
                        else
                        {
                            Console.Out.Write("Proceed? [Y/n]: ");
                            var yesOrNo1 = Console.ReadLine();
                            approved = yesOrNo1.Trim() == "Y";
                        }
                        if (approved)
                        {
                            Console.Out.WriteLine("Marking as read {0} items...", toMarkAsRead.Length);
                            var markAsReadRequest1 = new RestRequest("markers");
                            markAsReadRequest1.Method = Method.POST;
                            markAsReadRequest1.RequestFormat = DataFormat.Json;
                            markAsReadRequest1.AddBody(new { type = "entries", entryIds = toMarkAsRead.Select(item => item.FeedlyEntry.Id).ToArray(), action = "markAsRead" });
                            markAsReadRequest1.AddHeader("Authorization", "OAuth " + authToken);
                            var markAsReadResponse1 = client1.Execute(markAsReadRequest1);
                            if (markAsReadResponse1.ErrorException != null)
                            {
                                throw markAsReadResponse1.ErrorException;
                            }

                            Console.Out.WriteLine("Done!");
                        }
                        else
                        {
                            Console.Out.WriteLine("Doing nothing...");
                        }
                    }
                    else
                    {
                        Console.Out.WriteLine("No items to mark as read.");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error happended: {0}", e.Message);
                    Console.WriteLine(e.StackTrace);
                }
            }
            else
            {
                Console.Out.WriteLine("Unknown command");
                Environment.Exit(-1);
            }
        }

        private static List<Item> GetStreamItems(RestClient client, string categoryName, string userId, string authToken)
        {
            var request = new RestRequest("streams/contents");
            request.Method = Method.GET;
            request.AddHeader("Authorization", "OAuth " + authToken);
            var streamId = "user/" + userId + "/category/" + (string.IsNullOrEmpty(categoryName) ? "global.all" : categoryName);
            request.AddQueryParameter("streamId", streamId);
            request.AddQueryParameter("count", "1000");
            request.AddQueryParameter("unreadOnly", "true");

            Console.Out.WriteLine("Retrieving list of unread entries from Feedly...");
            var response = client.Execute<StreamReply>(request);
            if (response.ErrorException != null)
            {
                Console.Out.WriteLine("ERROR: Something went wrong:");
                throw response.ErrorException;
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Console.Out.WriteLine("ERROR: Server replied " + response.StatusCode);
                return new List<Item>();
            }
            var stream = response.Data;
            if (stream?.Items == null)
            {
                Console.Out.WriteLine("Nothing fetched!");
                return new List<Item>();
            }
            Console.Out.WriteLine("{0} items fetched!", stream.Items.Count);
            return stream.Items;
        }
    }

    public class StreamReply
    {
        public string Id { get; set; }
        public List<Item> Items { get; set; }

        public override string ToString()
        {
            return $"Id: {Id}, Items: {Items}";
        }
    }

    public class Item
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public int Engagement { get; set; }
        public long Crawled { get; set; }

        public DateTime CrawledDate
        {
            get
            {
                DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                return dtDateTime.AddMilliseconds(Crawled).ToLocalTime();
            }
        }

        public override string ToString()
        {
            return $"Id: {Id}, Title: {Title}, Engagement: {Engagement}";
        }
    }
}
