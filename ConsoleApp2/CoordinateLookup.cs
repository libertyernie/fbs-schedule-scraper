using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Net;

namespace ConsoleApp2 {
    public class StadiumCoordinates {
        public CoordinateLookup.Coordinates Stadium;
        public CoordinateLookup.Coordinates City;
        public string School;
        public bool FCS;

        public double Latitude => Stadium?.Latitude ?? City?.Latitude ?? 0;
        public double Longitude => Stadium?.Longitude ?? City?.Longitude ?? 0;

        public override string ToString() {
            return School + " - " + (Stadium?.Latitude != null
                ? Stadium?.ToString()
                : City?.ToString());
        }
    }

    public static class CoordinateLookup {
        public class GetPageResult {
            public string batchcomplete;
            public GetPageResultQuery query;
        }

        public class GetPageResultQuery {
            public Dictionary<long, Page> pages;
        }

        public class Page {
            public long pageid;
            public long ns;
            public string title;
            public Revision[] revisions;
        }

        public class Revision {
            public string contentformat;
            public string contentmodel;
            [JsonProperty(PropertyName = "*")]
            public string content;
        }

        private static HttpClient client = new HttpClient();

        private static async Task<string> GetWikipediaContent(string pageName) {
            var responseMessage = await client.GetAsync("https://en.wikipedia.org/w/api.php?action=query&prop=revisions&rvprop=content&format=json&redirects=1&titles=" + WebUtility.UrlEncode(pageName));
            string json = await responseMessage.Content.ReadAsStringAsync();
            var obj = JsonConvert.DeserializeObject<GetPageResult>(json);
            try {
                return obj.query.pages.Values.First().revisions.First().content;
            } catch (Exception e) {
                await Console.Error.WriteLineAsync(pageName + ": " + e.Message);
                return "";
            }
        }

        public static async Task<IEnumerable<StadiumCoordinates>> GetAllCoordinates() {
            if (File.Exists("locations.json")) {
                try {
                    return JsonConvert.DeserializeObject<IEnumerable<StadiumCoordinates>>(File.ReadAllText("locations.json"));
                } catch (Exception e) {
                    Console.Error.WriteLine(e.Message);
                    Console.Error.WriteLine(e.StackTrace);
                }
            }

            string content1 = await GetWikipediaContent("List of NCAA Division I FBS football stadiums");
            string content2 = await GetWikipediaContent("List of NCAA Division I FCS football stadiums");

            var tasks = new List<Task<StadiumCoordinates>>();

            Regex r = new Regex(@"\]\] ?\|\| ?\[\[");
            foreach (string content in new string[] { content1, content2 }) {
                bool fcs = content == content2;
                string x = content.Replace("||", "||\n");
                using (StringReader sr = new StringReader(x)) {
                    do {
                        string line = sr.ReadLine();
                        if (line == null || line.StartsWith("{|")) break;
                    } while (true);

                    Regex linkPattern = new Regex(@"\[\[([^\[\]\|]+)");
                    Regex displayPattern = new Regex(@"\[\[[^\|]+\|([^\]]+)");

                    Semaphore sem = new Semaphore(16, 16);
                    do {
                        string line = sr.ReadLine();
                        if (line == null || line == "==Future stadiums==") break;
                        if (line == "|-") {
                            string skip = sr.ReadLine();
                            while (skip == "") skip = sr.ReadLine();

                            string stadiumLink = sr.ReadLine();
                            var stadiumPage = linkPattern.Match(stadiumLink);
                            string stadiumName = stadiumPage.Groups[1].Value;
                            string cityLink = sr.ReadLine();
                            var cityPage = linkPattern.Match(cityLink);
                            string cityName = cityPage.Groups[1].Value;

                            sr.ReadLine();
                            string teamLink = sr.ReadLine();
                            var teamPage = displayPattern.Match(teamLink);
                            string teamDisplay = teamPage.Groups[1].Value.Replace("–", "-");

                            Console.WriteLine(stadiumName + ": " + cityName);

                            sem.WaitOne();
                            var task = GetStadiumCoordinates(stadiumName, cityName, teamDisplay, fcs);
                            var _ = task.ContinueWith(t => sem.Release());
                            tasks.Add(task);
                        }
                    } while (true);
                }
            }

            int i = 0;
            foreach (var task in tasks) {
                await task;
                Console.Write($"\r{++i}/{tasks.Count} ");
            }

            var result = tasks.Select(t => t.Result);
            File.WriteAllText("locations.json", JsonConvert.SerializeObject(result));
            return result;
        }

        public class Coordinates {
            public string Name;
            public double? Latitude;
            public double? Longitude;

            public Coordinates(string name, decimal? degN, decimal? degE) {
                if (degN < 0 || degE > 0) throw new ArgumentException($"This doesn't look right. ({name}, {degN}, {degE})");

                this.Name = name;
                this.Latitude = (double?)degN;
                this.Longitude = (double?)degE;

                if (this.Latitude != null) {
                    this.Latitude = Math.Round(this.Latitude.Value * 1000) / 1000;
                }
                if (this.Longitude != null) {
                    this.Longitude = Math.Round(this.Longitude.Value * 1000) / 1000;
                }
            }

            public override string ToString() {
                return $"{Name}: {Latitude?.ToString("#.###")}, {Longitude?.ToString("#.###")}";
            }
        }

        private static async Task<StadiumCoordinates> GetStadiumCoordinates(string stadiumName, string cityName, string schoolName, bool fcs) {
            return new StadiumCoordinates {
                Stadium = await GetCoordinates(stadiumName),
                City = await GetCoordinates(cityName),
                School = schoolName,
                FCS = fcs
            };
        }

        private static async Task<Coordinates> GetCoordinates(string pageName) {
            string content = await GetWikipediaContent(pageName);
            using (StringReader sr = new StringReader(content)) {
                do {
                    string line = sr.ReadLine();
                    if (line == null) break;
                    if (line.Replace(" ", "").StartsWith("|coordinates")) {
                        int index = line.IndexOf("{{coord|", StringComparison.InvariantCultureIgnoreCase);
                        if (index == -1) break;
                        line = line.Substring(index + 2);
                        line = line.Substring(0, line.IndexOf("}"));
                        string[] split = (line + "||||").Split('|');

                        decimal degN, minN, secN,
                                degE, minE, secE;

                        if (decimal.TryParse(split[1], out degN)
                            && decimal.TryParse(split[2], out minN)
                            && decimal.TryParse(split[3], out secN)
                            && decimal.TryParse(split[5], out degE)
                            && decimal.TryParse(split[6], out minE)
                            && decimal.TryParse(split[7], out secE)
                        ) {
                            // Deg/min/sec
                            degN += minN / 60;
                            degN += secN / 3600;
                            degE += minE / 60;
                            degE += secE / 3600;
                            if (split[4].Trim().ToUpperInvariant() == "S") degN = -degN;
                            if (split[8].Trim().ToUpperInvariant() == "W") degE = -degE;
                        } else if (decimal.TryParse(split[1], out degN)
                            && decimal.TryParse(split[2], out minN)
                            && decimal.TryParse(split[4], out degE)
                            && decimal.TryParse(split[5], out minE)
                        ) {
                            // Deg/min
                            degN += minN / 60;
                            degE += minE / 60;
                            if (split[3].Trim().ToUpperInvariant() == "S") degN = -degN;
                            if (split[6].Trim().ToUpperInvariant() == "W") degE = -degE;
                        } else if (decimal.TryParse(split[1], out degN)
                            && decimal.TryParse(split[3], out degE)
                        ) {
                            // Deg
                            if (split[2].Trim().ToUpperInvariant() == "S") degN = -degN;
                            if (split[4].Trim().ToUpperInvariant() == "W") degE = -degE;
                        } else if (decimal.TryParse(split[1], out degN)
                            && decimal.TryParse(split[2], out degE)
                        ) {
                            // Raw values
                        } else {
                            break;
                        }

                        return new Coordinates(pageName, degN, degE);
                    }
                } while (true);
            }
            return new Coordinates(pageName, null, null);
        }
    }
}
