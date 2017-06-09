using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ConsoleApp2 {
    public static class Schedule {
        private static HttpClient client = new HttpClient();

        //private static Dictionary<string, Task<HttpResponseMessage>> ScheduleCacheBySchool = new Dictionary<string, Task<HttpResponseMessage>>();

        private static async Task<List<string>> GetSchedule(int year, string url) {
            var responseMessage = await client.GetAsync($"http://www.fbschedules.com{url}");
            try {
                responseMessage.EnsureSuccessStatusCode();
            } catch (Exception e) {
                throw new Exception($"Could not download {url}", e);
            }
            var stream = await responseMessage.Content.ReadAsStreamAsync();
            using (var sr = new StreamReader(stream)) {
                var list = new List<string>();

                while (true) {
                    string line = await sr.ReadLineAsync();
                    if (line == null || line == "<table class=\"cfb-sch\">") break;
                }
                while (true) {
                    string line = await sr.ReadLineAsync();
                    if (line == null) break;

                    if (line.TrimStart().StartsWith("<td class=\"cfb2\">")) {
                        line = line.Substring(line.IndexOf("<strong>") + 8);
                        if (!line.Contains("</strong>")) {
                            line += await sr.ReadLineAsync();
                        }
                        line = line.Substring(0, line.IndexOf("<")).Trim();
                        list.Add(line);
                    }
                }

                return list;
            }
        }

        public static async Task<List<string>> GetSchedule(IEnumerable<int> years, string schoolAndTeamName) {
            var tasks = years.Select(async y => {
                string url = await GetScheduleUrlFor(schoolAndTeamName, y);
                if (url == null) return new List<string>(0);
                return await GetSchedule(y, url);
            }).ToList();
            var list = new List<string>();
            foreach (var task in tasks) {
                Console.WriteLine(string.Join(", ", await task));
                list.AddRange(await task);
            }
            return list;
        }

        private static Dictionary<string, Task<Dictionary<int, string>>> _scheduleUrlCache = new Dictionary<string, Task<Dictionary<int, string>>>();

        private static async Task<string> GetScheduleUrlFor(string schoolAndTeamName, int year) {
            Task<Dictionary<int, string>> dict;
            if (!_scheduleUrlCache.TryGetValue(schoolAndTeamName, out dict)) {
                dict = GetScheduleUrlsFor(schoolAndTeamName);
                _scheduleUrlCache.Add(schoolAndTeamName, dict);
            }

            var dictResult = await dict;
            if (dictResult.TryGetValue(year, out string url)) {
                return url;
            } else {
                return null;
            }
        }

        private static async Task<Dictionary<int, string>> GetScheduleUrlsFor(string schoolAndTeamName) {
            var dict = new Dictionary<int, string>();

            schoolAndTeamName = schoolAndTeamName.Replace(" ", "-").Replace("'", "").ToLowerInvariant();
            string url = $"http://www.fbschedules.com/ncaa-17/2017-{schoolAndTeamName}-football-schedule.php";
            var responseMessage = await client.GetAsync(url);
            try {
                responseMessage.EnsureSuccessStatusCode();
            } catch (Exception e) {
                throw new Exception($"Could not download {url}", e);
            }
            var stream = await responseMessage.Content.ReadAsStreamAsync();
            using (var sr = new StreamReader(stream)) {
                while (true) {
                    string line = await sr.ReadLineAsync();
                    if (line == null || line.Contains("<select name=\"menu1\"")) break;
                }
                while (true) {
                    string line = await sr.ReadLineAsync();
                    if (line == null) break;

                    int index = line.IndexOf("<option value=\"");
                    if (index == -1) break;

                    line = line.Substring(index + 15);
                    string path = line.Substring(0, line.IndexOf('"'));
                    string y = line.Substring(line.IndexOf('>') + 1);
                    y = y.Substring(0, y.IndexOf('<'));

                    dict.Add(int.Parse(y), path);
                }
            }

            return dict;
        }
    }
}
