using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using RestSharp;

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
                int minimalEngagement = -1;
                bool auto = false;
                int interval = 0;
                int minAge = 0;
                Queue<string> queue = new Queue<string>(args);
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
                        if (queueItem == "--interval-minutes")
                        {
                            interval = Convert.ToInt32(queue.Dequeue());
                        }
                        if (queueItem == "--min-entry-age-days")
                        {
                            minAge = Convert.ToInt32(queue.Dequeue());
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR: Could not parse command line parameters! Details:");
                    throw;
                }
                do
                {
                    Console.Out.WriteLine("---------------------------------");
                    Console.Out.WriteLine("Executing Mark-As-Read command with parameters:");
                    Console.Out.WriteLine("Category: '{0}'", category);
                    Console.Out.WriteLine("Minimal engagement level: {0}", minimalEngagement > 0 ? minimalEngagement.ToString() : "None");
                    Console.Out.WriteLine("Minimal entry age: {0}", minAge);
                    Console.Out.WriteLine("Repeat: every {0} minutes", interval);
                    Console.Out.WriteLine("Mark as read automatically: {0}", auto);
                    Console.Out.WriteLine("---------------------------------");

                    var client1 = new RestClient {BaseUrl = new Uri("https://cloud.feedly.com/v3/")};
                    //var unreadItItems1 = GetStreamItems(client1, "A - Programming").Concat(GetStreamItems(client1, "Engineering Blogs")).Concat(GetStreamItems(client1, "A - IT Many News"));
                    //                var unreadNewsItems1 = GetStreamItems(client1, "A - News");
                    var unreadNewsItems1 = GetStreamItems(client1, category, userId, authToken);
                    //var stopWords1 = new[] { "icymi", "youtrack", "wordpress", "yii", "php", "ruby", "objective-c", "clojure", "kotlin", "laravel", "watchos", "zfs", "rocksdb", "xcode", "ionic", " rails", "gcc", "collective #", "sponsored post" };
                    //                var toMarkAsRead1 = unreadItItems1.Where(item => item?.Title != null).Where(item => { return stopWords1.Any(sw => item.Title.ToLower().Contains(sw.ToLower())); }).Concat().ToArray();
                    Console.Out.WriteLine("Selecting items to mark as read...");
                    var toMarkAsRead1 = unreadNewsItems1.Where(item => item.Engagement < minimalEngagement && item.CrawledDate.CompareTo(DateTime.UtcNow.AddDays(-minAge)) < 0).OrderBy(item => item.CrawledDate).ToArray();
                    if (toMarkAsRead1.Length > 0)
                    {
                        foreach (var item1 in toMarkAsRead1)
                        {
                            Console.Out.WriteLine("Want to mark as read: " + item1.CrawledDate + " " + item1.Title.Replace("\n", "") + " [" + item1.Engagement + "]");
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
                            Console.Out.WriteLine("Marking as read {0} items...", toMarkAsRead1.Length);
                            var markAsReadRequest1 = new RestRequest("markers");
                            markAsReadRequest1.Method = Method.POST;
                            markAsReadRequest1.RequestFormat = DataFormat.Json;
                            markAsReadRequest1.AddBody(new {type = "entries", entryIds = toMarkAsRead1.Select(item => item.Id).ToArray(), action = "markAsRead"});
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
                    if (interval > 0)
                    {
                        Console.Out.WriteLine("Waiting for {0} minutes to repeat...", interval);
                        Thread.Sleep(TimeSpan.FromMinutes(interval));
                    }
                } while (interval > 0);
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
            request.AddQueryParameter("streamId", "user/" + userId + "/category/" + categoryName);
            request.AddQueryParameter("count", "1000");
            request.AddQueryParameter("unreadOnly", "true");

            Console.Out.WriteLine("Retrieving list of unread entries from Feedly...");
            var response = client.Execute<StreamReply>(request);
            if (response.ErrorException != null)
            {
                Console.Out.WriteLine("ERROR: Something went wrong:");
                throw response.ErrorException;
            }
            var stream = response.Data;
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
