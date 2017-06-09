using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp2 {
    class Program {
        static HttpClient client = new HttpClient();

        public static async Task<Image> GetMap() {
            var response = await client.GetAsync("https://upload.wikimedia.org/wikipedia/commons/thumb/3/33/USA_location_map.svg/1024px-USA_location_map.svg.png");
            Stream inStream = await response.Content.ReadAsStreamAsync();
            return Image.FromStream(inStream);
        }

        public static Image Overlay(Image image, string name, double latitude, double longitude, bool fcs, double opacity = 1.0, bool bold = false) {
            using (Graphics g = Graphics.FromImage(image)) {
                double centerY = (latitude - 24.2) / (49.8 - 24.2);
                double centerX = (longitude - -125.5) / (-66.5 - -125.5);
                Console.WriteLine(centerX + " " + centerY);
                centerY = 1 - centerY;
                centerX *= image.Width;
                centerY *= image.Height;
                Color color = Color.FromArgb(Math.Min(255, (int)(128 + opacity * 128)), fcs ? 255 : 0, 0, 0);
                float fontSize = 10 + (float)(4 * opacity);
                g.DrawString(name, new Font("Arial", fontSize, bold ? FontStyle.Bold : FontStyle.Regular), new SolidBrush(color), (float)centerX, (float)centerY, new StringFormat() {
                    LineAlignment = StringAlignment.Center,
                    Alignment = StringAlignment.Center
                });
                return image;
            }
        }
        
        static StadiumCoordinates FindSchool(IEnumerable<StadiumCoordinates> list, string name) {
            name = WebUtility.HtmlDecode(name);

            // for Texas Tech weird HTML
            if (name.Contains("<a href")) {
                name = name.Substring(name.LastIndexOf('>') + 1);
            }

            if (name == "") return null;
            if (name == "OFF") return null;
            if (name == "BYE") return null;
            if (name == "Open") return null;
            if (name == "Open Date") return null;
            if (name.Contains("Championship")) return null;

            // Normalize some FCS team names
            foreach (string s in new string[] {
                "Campbell",
                "Western Illinois",
                "Gardner-Webb",
                "Stephen F. Austin",
                "Eastern Washington",
                "Stony Brook",
            }) {
                if (name.IndexOf(s, StringComparison.InvariantCultureIgnoreCase) == 0) name = s;
            }

            if (string.Equals(name, "USC Trojans", StringComparison.InvariantCultureIgnoreCase)) name = "Southern California";
            if (string.Equals(name, "Ole Miss Rebels", StringComparison.InvariantCultureIgnoreCase)) name = "Mississippi";
            if (string.Equals(name, "UConn Huskies", StringComparison.InvariantCultureIgnoreCase)) name = "Connecticut";
            if (name.IndexOf("Ragin' Cajuns", StringComparison.InvariantCultureIgnoreCase) > -1) name = "Louisiana-Lafayette";
            if (string.Equals(name, "ULM Warhawks", StringComparison.InvariantCultureIgnoreCase)) name = "Louisiana-Monroe";
            if (string.Equals(name, "Delaware Blue Hens", StringComparison.InvariantCultureIgnoreCase)) name = "Delaware Fightin' Blue Hens";
            if (string.Equals(name, "Northern Illinois Huskies", StringComparison.InvariantCultureIgnoreCase)) name = "NIU";
            if (string.Equals(name, "North Dakota", StringComparison.InvariantCultureIgnoreCase)) name = "North Dakota Fighting Hawks";
            if (name.IndexOf("Southeast Missouri", StringComparison.InvariantCultureIgnoreCase) == 0) name = "Southeast Missouri State Redhawks";
            if (name.IndexOf("NC Central", StringComparison.InvariantCultureIgnoreCase) == 0) name = "North Carolina Central";
            if (name.IndexOf("CCSU", StringComparison.InvariantCultureIgnoreCase) == 0) name = "Central Connecticut";
            if (name.IndexOf("Francis (Pa.)", StringComparison.InvariantCultureIgnoreCase) > -1) name = "Saint Francis";
            if (name.IndexOf("NAU", StringComparison.InvariantCultureIgnoreCase) == 0) name = "Northern Arizona";

            int i = 0;
            string n = name.Trim();
            while (true && i < 100) {
                if (n == "") break;

                var team = list.Where(s => s.School.Equals(n, StringComparison.InvariantCultureIgnoreCase));
                if (team.Count() == 1) return team.Single();

                n = n.Substring(0, n.LastIndexOf(" ") + 1).Trim();
                i++;
            }
            var q = list.Where(s => s.School.StartsWith(name, StringComparison.InvariantCultureIgnoreCase));
            if (q.Count() == 1) return q.Single();

            throw new Exception("Team not found: " + name);
        }

        static void Main(string[] args) {
            var c = CoordinateLookup.GetAllCoordinates().Result;
            
            // UMass has two stadiums
            c = c.Where(x => !x.City.Name.Contains("Foxborough"));
            // Hawaii won't fit on the map
            c = c.Where(x => !x.School.Contains("Hawai"));
            c = c.Concat(Enumerable.Repeat(new StadiumCoordinates {
                Stadium = new CoordinateLookup.Coordinates("(Hawaii)", 25, -124),
                School = "Hawaii"
            }, 1));

            c = c.Concat(new StadiumCoordinates[] {
                // Schools that no longer have football programs
                new StadiumCoordinates {
                    Stadium = new CoordinateLookup.Coordinates("Parsons Field", 42.336944m, -71.113889m),
                    School = "Northeastern Huskies"
                },
                new StadiumCoordinates {
                    Stadium = new CoordinateLookup.Coordinates("James M. Shuart Stadium", 40.715833m, -73.596389m),
                    School = "Hofstra Pride"
                },
                // UTSA played NW Oklahoma State in 2012
                new StadiumCoordinates {
                    Stadium = new CoordinateLookup.Coordinates("Alva, Oklahoma", 36.805833m, -98.667778m),
                    School = "NW Oklahoma State Rangers"
                }
            });

            //foreach (string teamName in File.ReadAllLines(@"teams.txt")) {
            while (true) {
                Console.WriteLine("Type a team name (e.g. Wisconsin Badgers) or \"quit\" to quit");
                string teamName = Console.ReadLine();
                if (teamName.Equals("quit", StringComparison.CurrentCultureIgnoreCase)) break;
                if (teamName == "") continue;
                Console.WriteLine("Type a starting year (e.g. 2008)");
                if (!int.TryParse(Console.ReadLine(), out int startYear)) startYear = 2008;
                Console.WriteLine("Type an ending year (e.g. 2017)");
                if (!int.TryParse(Console.ReadLine(), out int endYear)) endYear = 2017;

                var thisTeam = FindSchool(c, teamName);

                string pngFile = (thisTeam.School.ToLower().Replace(" ", "-")) + ".png";
                if (File.Exists(pngFile)) continue;

                Task.Run(async () => {
                    try {
                        var image = await GetMap();

                        //List<string> teams = new List<string>();
                        //bool first = true;
                        //while (true) {
                        //    Console.WriteLine("Type the name of a school, or leave blank to end." + (first ? " The first name will be in bold." : ""));
                        //    string line = Console.ReadLine();
                        //    if (line == "") break;

                        //    var q = c.Where(s => s.School.Equals(line.Trim(), StringComparison.InvariantCultureIgnoreCase));
                        //    if (!q.Any()) {
                        //        Console.WriteLine("Not found: " + line);
                        //    } else if (q.Count() > 1) {
                        //        Console.WriteLine("Multiple matches found: " + string.Join(", ", q.Select(s => s.School)));
                        //    } else {
                        //        var team = q.First();
                        //        image = Overlay(image, team.School, team.Latitude, team.Longitude, bold: first);
                        //        first = false;
                        //    }
                        //}

                        int yearCount = endYear - startYear + 1;

                        var list = await Schedule.GetSchedule(Enumerable.Range(startYear, yearCount), teamName);

                        foreach (var group in list.Select(s => s.StartsWith("at ") ? s.Substring(3) : s).Select(s => FindSchool(c, s)).GroupBy(x => x)) {
                            var team = group.Key;
                            if (team != null) {
                                image = Overlay(image, team.School, team.Latitude, team.Longitude, team.FCS, opacity: (double)group.Count() / yearCount);
                            }
                        }

                        image = Overlay(image, thisTeam.School, thisTeam.Latitude, thisTeam.Longitude, thisTeam.FCS, opacity: 2, bold: true);

                        image.Save(pngFile, ImageFormat.Png);
                        Process.Start(pngFile);
                    } catch (Exception e) {
                        Console.Error.WriteLine(e.Message);
                        Console.Error.WriteLine(e.StackTrace);
                    }
                }).GetAwaiter().GetResult();
            }
        }
    }
}
