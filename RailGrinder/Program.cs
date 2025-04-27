using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web;
using static System.Net.WebRequestMethods;

namespace RailGrinder
{
    internal class Program
    {
        //********************************
        //DEVELOPMENT ROADMAP:
        //to-do: Improve the ROI calculation: Stats-based??
        //to-do: Adjust ROI for missed notes somehow
        //to-do: Filter all but the top-score for each user for different mod combos in spiral & spin
        //to-do: Improve spin query to capture all common mod combos
        //to-do: Shift ALL calls to the scores api to permit faster calls of up to 100 scores per api call.

        static string cmd_template = """https://synthriderz.com/api/rankings?s={"mode":§mode§,"difficulty":§difficulty§,"modifiers":§modifiers§,"profile.id":§id§}&page=1&limit=10&sort=rank,ASC""";

        static string userid_template = """https://synthriderz.com/api/rankings?s={"mode":§mode§,"difficulty":§difficulty§,"modifiers":§modifiers§,"profile.name":"§name§"}&page=1&limit=10&sort=rank,ASC""";

        //Returns the top scores set by the player for an mode, difficulty, and modifers
        static string personal_template = """https://synthriderz.com/api/scores?join[]=leaderboard&join[]=leaderboard.beatmap&join[]=profile&join[]=profile.user&sort=rank,ASC&page=§page§&limit=100&s={"$and":[{"beatmap.published":true},{"profile.id":§userid§},{"leaderboard.mode":§mode§},{"leaderboard.difficulty":§difficulty§},{"modifiers":§modifiers§},{"leaderboard.beatmap.ost":true},{"leaderboard.challenge":0}]}""";

        // Returns the leaderboard for a particular leaderboard.id, just like the in-game scoreboard would show
        static string leaderboard_template = @"https://synthriderz.com/api/leaderboards/§id§/scores?limit=10&page=§page§&modifiers=§modifiers§&sort=rank,ASC";

        //Returns the combined modifiers leaderboard (like for spin & spiral)
        static string combined_modifers_template = """https://synthriderz.com/api/scores?join[]=leaderboard&join[]=leaderboard.beatmap&join[]=profile&join[]=profile.user&sort=modified_score,DESC&page=§page§&limit=100&s={"$and":[{"leaderboard.id":§id§},{"modifiers":{"$in":[§modifiers§]}}]}""";

        //Examples:
        //   https://synthriderz.com/api/leaderboards/7297/scores?limit=10&page=0&modifiers=-1&sort=rank,ASC
        //   https://synthriderz.com/api/scores?join[]=leaderboard&join[]=leaderboard.beatmap&join[]=profile&join[]=profile.user&sort=rank,ASC&page=1&limit=100&s={%22$and%22:[{%22leaderboard.id%22:7297},{%22modifiers%22:{%22$in%22:[1,2,4,8]}}]}

        static string all_leaderboards_template = """https://synthriderz.com/api/leaderboards?join[]=beatmap&page=§page§&limit=100&s={"$and":[{"beatmap.published":true},{"mode":§mode§},{"difficulty":§difficulty§},{"beatmap.ost":true},{"challenge":0}]}""";


        //Declare the variables for leaderboard overall metrics
        static double average_poor = 0;
        static double average_good = 0;
        static double average_perfect = 0;
        static double average_accuracy = 0;
        static double average_rank = 0;
        static double average_rank_combined = 0;
        static double stddev_poor = 0;
        static double stddev_good = 0;
        static double stddev_perfect = 0;
        static double stddev_accuracy = 0;
        static double stddev_rank = 0;
        static double stddev_rank_combined = 0;

        static async Task Main(string[] args)
        {
            if (args.Length != 5)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Rail");
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write("Grinder ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("v");
                //Pull the version number from the project file. Edit the project file to update.
                Console.Write(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Assembly.GetExecutingAssembly().GetName().Version.ToString());
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("by Nova_Max and Marinus");
                Console.WriteLine("");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("This tool is designed to help analyze a player's performance based on leaderboard scores and find the best chances for");
                Console.WriteLine("ranking improvement. This really hammers the Synthriderz api, so please be kind and don't abuse or over-use it.");
                Console.WriteLine("");
                Console.ForegroundColor = ConsoleColor.Gray;
            }
            Console.ForegroundColor = ConsoleColor.White;

            List<dynamic> personal_leaderboard = new List<dynamic>();
            List<dynamic> all_leaderboards = new List<dynamic>();

            int userid = 224725;
            int difficulty = 4;
            int mode = 1;
            string modifier = "0";  //What the user enters
            string modifiers = "0"; //What the user's value is converted into for the api query
            bool played = true;
            bool average = false;
            string dm;
            int rank;
            string username = "";
            string SummaryHeading = "";

            // Pair each modifier value with its textual name:
            (int Value, string Name)[] ModifierMap =
            {
                (1,       "Spin90"),
                (2,       "Spin180"),
                (4,       "Spin360"),
                (8,       "Spin360Plus"),
                (16,      "NoFail"),
                (32,      "NoObstacles"),
                (64,      "HaloNotes"),
                (128,     "NJS2x"),
                (256,     "NJS3x"),
                (512,     "SuddenDeath"),
                (1024,    "PrismaticNotes"),
                (2048,    "VanishNotes"),
                (4096,    "SmallNotes"),
                (8192,    "BigNotes"),
                (16384,   "SpinStyled"),
                (32768,   "SpinWild"),
                (131072,  "SpiralMild"),
                (262144,  "SpiralStyled"),
                (524288,  "SpiralWild")
            };
            if (args.Length == 1 && (args[0] == "-?" || args[0] == "-help"))
            {
                Console.WriteLine("Usage: RailGrinder [userid] [difficulty] [mode] [modifiers] [output path]");
                Console.WriteLine("[userid]:     <Integer>");
                Console.WriteLine("[difficulty]: 0:Easy - 1:Normal - 2:Hard - 3:Expert - 4:Master");
                Console.WriteLine("[mode]:       0:Rhythm - 1:Force");
                //Console.WriteLine("[modifiers]:  0:No Modifiers - 1:Combined - 2:Spin (all) - 3:Spiral (all)");
                Console.WriteLine("[modifiers]:  0:No Modifiers - 1:Combined");
                Console.WriteLine("[path] (optional):  Path to save summary of averages");
                return;
            }

            using (WebClient client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;

                // **************************************************************************************
                // Execute this analysis based on command line arguments
                // **************************************************************************************
                // Note: This has not been tested since initiating major revisions and may not be fully functional.
                if (args.Length > 3)
                {
                    userid = Convert.ToInt32(args[0]);
                    difficulty = Convert.ToInt32(args[1]);
                    mode = Convert.ToInt32(args[2]);
                    modifiers =
                        Convert.ToInt32(args[3]) == 0 ? "0" :
                        Convert.ToInt32(args[3]) == 1 ? "{}" :
                        "0";

                    // Calculate and save the basic statistics to file if a path is provided
                    if (args.Length == 5)
                    {

                        string req = cmd_template;
                        req = req.Replace("§difficulty§", difficulty.ToString());
                        req = req.Replace("§mode§", mode.ToString());
                        req = req.Replace("§modifiers§", modifiers);
                        req = req.Replace("§id§", userid.ToString());

                        string resp = await client.DownloadStringTaskAsync(req);
                        dynamic res_data = JObject.Parse(resp);


                        average_rank = res_data.data[0].rank_avg;

                        average_poor = res_data.data[0].poor_hit_percent;
                        average_good = res_data.data[0].good_hit_percent;
                        average_perfect = res_data.data[0].perfect_hit_percent;

                        average_accuracy = average_poor * 0.25 + average_good * 0.5 + average_perfect;

                        WriteFile(args[4]);

                        return;
                    }
                }
                else

                // **************************************************************************************
                // Prompt the user to provide the desired options for this analysis in the command line
                // **************************************************************************************
                {
                    //#if !DEBUG 
                    Console.WriteLine("Select Search Operation: ");
                    Console.WriteLine("1: Unplayed Maps");
                    Console.WriteLine("2: Played Maps");
                    Console.WriteLine("3: Average");
                    bool invalid = false;
                    do
                    {
                        invalid = false;
                        string play = Console.ReadLine();
                        if (play == "1")
                        {
                            played = false;
                        }
                        else if (play == "2")
                        {
                            played = true;
                        }
                        else if (play == "3")
                        {
                            average = true;
                        }
                        else
                        {
                            invalid = true;
                            Console.WriteLine("Invalid input, try again: ");
                        }
                    } while (invalid);

                    Console.WriteLine("");
                    Console.WriteLine("Select Difficulty: ");
                    Console.WriteLine("1: Easy");
                    Console.WriteLine("2: Normal");
                    Console.WriteLine("3: Hard");
                    Console.WriteLine("4: Expert");
                    Console.WriteLine("5: Master (default)");
                    do
                    {
                        invalid = false;
                        string num = Console.ReadLine();
                        if (num == "1")
                        {
                            difficulty = 0;
                        }
                        else if (num == "2")
                        {
                            difficulty = 1;
                        }
                        else if (num == "3")
                        {
                            difficulty = 2;
                        }
                        else if (num == "4")
                        {
                            difficulty = 3;
                        }
                        else if (num == "5" || num == "")
                        {
                            difficulty = 4;
                        }
                        else
                        {
                            invalid = true;
                            Console.WriteLine("Invalid input, try again: ");
                        }
                    } while (invalid);

                    Console.WriteLine("");
                    Console.WriteLine("Select Mode: ");
                    Console.WriteLine("1: Rhythm");
                    Console.WriteLine("2: Force");
                    do
                    {
                        invalid = false;
                        string mod = Console.ReadLine();
                        if (mod == "1")
                        {
                            mode = 0;
                        }
                        else if (mod == "2")
                        {
                            mode = 1;
                        }
                        else
                        {
                            invalid = true;
                            Console.WriteLine("Invalid input, try again: ");
                        }
                    } while (invalid);

                    Console.WriteLine("");
                    Console.WriteLine("Select Modifiers: ");
                    Console.WriteLine("1: No Modifiers");
                    Console.WriteLine("2: Combined Overall");
                    Console.WriteLine("3: Spin (all) - still under development and not yet fully functional.");
                    Console.WriteLine("4: Spiral (all) - still under development and not yet fully functional.");
                    do
                    {
                        invalid = false;
                        modifier = Console.ReadLine();
                        //No Modifiers:
                        //    rankings api call requires "modifiers":0 
                        //    personal leaderboard api call requires "modifiers":0 
                        //    leaderboards api call requires modifiers=0
                        //Combined:
                        //    rankings api call requires "modifiers":{} 
                        //    personal leaderboard api call requires "modifiers":{} 
                        //    leaderboards api call requires modifiers=-1 [This is the last api called by this program, so we'll change the value of this variable from "{}" to "-1" before we query the api]
                        //
                        //Spin/Spiral:
                        //    The $in code with brute force lists of every possible Spiral modifier combination and most Spin modifiers permits combined Spin(all) and combined Spiral(all) rankings and analysis.
                        //    More advanced bit logic methods are used for filtering personal leaderboard once the full leaderboard is compiled to a single array.
                        //
                        //Note: The $in list is not 100% comprehensive, and only represents the most common modes used by scorechasers (2x, 3x, big, small).
                        //Every additional will double the length of the list. Maybe that's OK? Prisma, halo, nowalls, and nofail are presently excluded.
                        //I suppose we can experiment and TRY all 5184 combos, etc, and see at what point this breaks. The leaderboard API call appears to be working with 4 modes for spin and 27 modes/combos for spiral when manually called using the string below I was testing with.
                        //Test API call (all spins, vanilla mild): https://synthriderz.com/api/scores?join[]=leaderboard&join[]=leaderboard.beatmap&join[]=profile&join[]=profile.user&sort=rank,ASC&page=1&limit=10&s={%22$and%22:[{%22beatmap.published%22:true},{%22profile.id%22:1698739},{%22leaderboard.mode%22:0},{%22leaderboard.difficulty%22:4},{%22modifiers%22:{%22$in%22:[1,2,4,8]}},{%22leaderboard.beatmap.ost%22:true},{%22leaderboard.challenge%22:0}]}
                        //
                        //Leaderboard Examples:
                        //  Regular call: https://synthriderz.com/api/leaderboards/7297/scores?limit=10&page=0&modifiers=-1&sort=rank,ASC
                        //  Spiral:
                        //
                        //Spin: 3 modes + top 2 modifiers: 108 combinations. All mods: 5184 combinations.
                        //    For multiple modifiers: {"$in":[1,2,3,8]} //+additional 
                        //Spiral: 3 modes + top 2 modifiers: 27 combinations.  All mods: 1296 combinations
                        //    For all spiral including mild styled wild (vanilla) 2x 3x bignotes smallnotes: {"$in":[131072,135168,139264,131200,135296,139392,131328,135424,139520,262144,266240,270336,262272,266368,270464,262400,266496,270592,524288,528384,532480,524416,528512,532608,524544,528640,532736]}
                        //
                        if (modifier == "1" || modifier == "")
                        {
                            modifiers = "0";
                        }
                        else if (modifier == "2" || modifier == "3" || modifier == "4")
                        {
                            modifiers = "{}";
                        }
                        //We can filter for spin & spiral later. All top plays oif all modes will returned in this combined leaderboard.
                        //else if (modifier == "3")
                        //{
                        //    modifiers = "{\"$in\":[1,2,4,8,need to add the rest]}";
                        //}
                        //else if (modifier == "4")
                        //{
                        //    modifiers = "{\"$in\":[131072,135168,139264,131200,135296,139392,131328,135424,139520,262144,266240,270336,262272,266368,270464,262400,266496,270592,524288,528384,532480,524416,528512,532608,524544,528640,532736]}";
                        //}
                        else
                        {
                            invalid = true;
                            Console.WriteLine("Invalid input, try again: ");
                        }
                    } while (invalid);

                    //Prompt for a username, and then check to see if the username is valid and unique.
                    //If no leaderboards are found for that username, ask again.
                    //If multiple leaderboards are discovered for the same username, ask which one to use.
                    Console.WriteLine("");
                    Console.WriteLine("Enter Username (Capitalization Matters): ");
                    do
                    {
                        invalid = false;
                        username = Console.ReadLine();

                        try
                        {
                            string req = userid_template;
                            req = req.Replace("§difficulty§", difficulty.ToString());
                            req = req.Replace("§mode§", mode.ToString());
                            req = req.Replace("§modifiers§", modifiers);
                            req = req.Replace("§name§", HttpUtility.UrlEncode(username));
                            //Console.WriteLine("userid_template: " + req);

                            string resp = await client.DownloadStringTaskAsync(req);
                            dynamic res_data = JObject.Parse(resp);
                            IEnumerable<dynamic> data = res_data.data;
                            var rankings = data.GroupBy(x => x.profile.id).Select(x => x.First()).ToList();
                            int index = 0;
                            if (rankings.Count > 1)
                            {
                                Console.WriteLine("Multiple users found with that name: ");
                                int count = 0;
                                foreach (var ranking in rankings)
                                {
                                    count++;
                                    Console.WriteLine(count + ": " + ranking.profile.id.ToString().PadRight(9, ' ') + " rank: " + ranking.rank);
                                }
                                Console.WriteLine("Please enter the one you want to use: ");
                                int selected = Convert.ToInt32(Console.ReadLine());
                                if (selected > rankings.Count || selected <= 0)
                                {
                                    invalid = true;
                                    Console.WriteLine("Invalid input, try again: ");
                                }
                                index = selected - 1;
                            }
                            if (rankings.Count > 0)
                            {
                                //This is not giving accurate values for combined leaderboards. I think it's including mods other than the top combined score.
                                //We recalculate this later for the specific selections. But it's negligible compute cost to leave it alone here vs risking breaking something.
                                userid = rankings[index].profile.id;
                                average_rank = rankings[index].rank_avg;

                                average_poor = rankings[index].poor_hit_percent;
                                average_good = rankings[index].good_hit_percent;
                                average_perfect = rankings[index].perfect_hit_percent;

                                average_accuracy = average_poor * 0.25 + average_good * 0.5 + average_perfect;
                            }
                            else
                            {
                                invalid = true;
                                Console.WriteLine("User not found, try again: ");
                            }
                        }
                        catch (Exception e)
                        {
                            invalid = true;
                            Console.WriteLine("Error, try again: ");
                        }
                    } while (invalid);
                }
                SummaryHeading = username + ": ";
                if (mode == 0) { SummaryHeading = SummaryHeading + "Rhythm, "; }
                if (mode == 1) { SummaryHeading = SummaryHeading + "Force, "; };
                if (difficulty == 0) { SummaryHeading = SummaryHeading + "Easy, "; }
                if (difficulty == 1) { SummaryHeading = SummaryHeading + "Normal, "; }
                if (difficulty == 2) { SummaryHeading = SummaryHeading + "Hard, "; }
                if (difficulty == 3) { SummaryHeading = SummaryHeading + "Expert, "; }
                if (difficulty == 4) { SummaryHeading = SummaryHeading + "Master, "; }
                if (modifier == "1") { SummaryHeading = SummaryHeading + "No Modifers"; }
                if (modifier == "2") { SummaryHeading = SummaryHeading + "Overall Combined Modifers"; }
                if (modifier == "3") { SummaryHeading = SummaryHeading + "Spin (all)"; }
                if (modifier == "4") { SummaryHeading = SummaryHeading + "Spiral (all)"; }


                // **************************************************************************************
                // Build a list of all maps the user has played in the selected  difficulty/mode/mods
                // **************************************************************************************

                Console.ForegroundColor = ConsoleColor.Gray;

                Console.WriteLine("User ID: " + userid);
                Console.WriteLine("");
                Console.WriteLine("Loading Personal Leaderboards (100 scores per page)");

                //v1 pulled averages from the api, but these were only accurate for the nomods leaderboard. We want better statistical data anyway.

                //if (!average)
                //{
                int page = 0;
                int pages = 0;

                do
                {
                    page++;
                    //Console.WriteLine("Personal Leaderboard page: " + page + " of " + pages);
                    Console.Write("Personal Leaderboard page: " + page + " of ");
                    string req = personal_template;
                    req = req.Replace("§page§", page.ToString());
                    req = req.Replace("§userid§", userid.ToString());
                    req = req.Replace("§difficulty§", difficulty.ToString());
                    req = req.Replace("§mode§", mode.ToString());
                    req = req.Replace("§modifiers§", modifiers);
                    //Console.WriteLine("Personal_template:" + req);

                    string resp = await client.DownloadStringTaskAsync(req);
                    dynamic res_data = JObject.Parse(resp);

                    personal_leaderboard.AddRange(res_data.data);
                    page = res_data.page;
                    pages = res_data.pageCount;
                    Console.WriteLine(pages);
                } while (page < pages);

                // the Z.api response includes a line for every modified combination the player has set a score on. We only care about the best of these.
                // We will sort the personal leaderboard by modified_score descending to get the top scores first, and discard the rest.
                // the req api call is hardcoded to sort by rank, so no sort or filter is necessary here for nomods.
                if (modifier == "3")
                {
                    // SPIN
                    // Filter for spin bits (1, 2, 4, 8), and sort descending by modified score, best scores first.
                    personal_leaderboard = personal_leaderboard.Where(x => ((int)x.modifiers & (1 | 2 | 4 | 8)) != 0).GroupBy(x => x.leaderboard.beatmap.id).Select(x => x.OrderByDescending(y => y.modified_score).FirstOrDefault()).ToList();
                }
                else if (modifier == "4")
                {
                    // SPIRAL
                    // Filter for spiral bits (SpinMild = 131072, SpiralStyled = 262144, SpiralWild = 524288), and sort descending by modified score, best scores first.
                    personal_leaderboard = personal_leaderboard.Where(x => ((int)x.modifiers & (131072 | 262144 | 524288)) != 0).GroupBy(x => x.leaderboard.beatmap.id).Select(x => x.OrderByDescending(y => y.modified_score).FirstOrDefault()).ToList();
                }
                else //if (modifier == "2") or anything else, just give the combined leaderboard. All queries, whether nomod, single mod, or combinations, can be sorted by modified_score.
                {
                    // COMBINED
                    personal_leaderboard = personal_leaderboard.GroupBy(x => x.leaderboard.beatmap.id).Select(x => x.OrderByDescending(y => y.modified_score).Where(x => x.modified_score > 0).First()).ToList();
                }


                //RECALCULATE AVERAGES HERE FROM PERSONAL LEADERBOARD
                //Relevant scores api json fields: rank, rank_combined, good_hit_percent, poor_hit_percent, perfect_hit_percent, [ notes_hit & max_combo ]
                if (modifier == "1")
                {
                    average_rank = personal_leaderboard.Average(x => (double)((JToken)x)["rank"]);
                    stddev_rank = StandardDeviation(personal_leaderboard.Select(x => (double)((JToken)x)["rank"]));
                }
                else if (modifier == "2")
                {
                    average_rank_combined = personal_leaderboard.Average(x => (double)((JToken)x)["rank_combined"]);
                    stddev_rank_combined = StandardDeviation(personal_leaderboard.Select(x => (double)((JToken)x)["rank_combined"]));
                } //We need to recalculate spin & spiral ranks before we can calculate statistics. They will sit at 0 for now.

                average_poor = personal_leaderboard.Average(x => (double)((JToken)x)["poor_hit_percent"]);
                average_good = personal_leaderboard.Average(x => (double)((JToken)x)["good_hit_percent"]);
                average_perfect = personal_leaderboard.Average(x => (double)((JToken)x)["perfect_hit_percent"]);
                stddev_poor = StandardDeviation(personal_leaderboard.Select(x => (double)((JToken)x)["poor_hit_percent"]));
                stddev_good = StandardDeviation(personal_leaderboard.Select(x => (double)((JToken)x)["good_hit_percent"]));
                stddev_perfect = StandardDeviation(personal_leaderboard.Select(x => (double)((JToken)x)["perfect_hit_percent"]));
                average_accuracy = average_poor * 0.25 + average_good * 0.5 + average_perfect;
                stddev_accuracy = stddev_poor * 0.25 + stddev_good * 0.5 + stddev_perfect;

                if (!average)
                {

                    // **************************************************************************************
                    // Analyze the performance and ranking of played maps for opportunities to improve
                    // **************************************************************************************
                    if (played)
                    {
                        int count = 0;
                        List<dynamic> results = new List<dynamic>();
                        Console.WriteLine("");
                        Console.WriteLine("Analyzing leaderboards for each map. This list represents each Rank(Modifiers) plus analysis of each page of higher ranked results (10 secores per .).");
                        // None = 0, Spin90 = 1, Spin180 = 2, Spin360 = 4, Spin360Plus = 8, NoFail = 16, NoObstacles = 32, HaloNotes = 64, NJS2x = 128, NJS3x = 256, SuddenDeath = 512, PrismaticNotes = 1024, VanishNotes = 2048, SmallNotes = 4096, BigNotes = 8192, SpinStyled = 16384, SpinWild = 32768, SpiralMild = 131072, SpiralStyled = 262144, SpiralWild = 524288

                        foreach (var i in personal_leaderboard)
                        {
                            count++;
                            //Console.Write("Map " + count + " of " + personal_leaderboard.Count);


                            //Convert the modifier integer into a descriptive string
                            //bool success = int.TryParse((string)i.modifiers, out int intmodifers);
                            if (i.modifiers == 0)
                            {
                                dm = "";
                            }
                            else
                            {
                                dm = ": ";

                                //var result = new List<string>();
                                int tm = i.modifiers;

                                for (int j = ModifierMap.Length - 1; j >= 0; j--)
                                {
                                    var (value, name) = ModifierMap[j];
                                    if (tm >= value)
                                    {
                                        if (dm != ": ")
                                        {
                                            dm = dm + (", ");
                                        };
                                        dm = dm + name;
                                        tm = tm - value;
                                    }
                                }

                            }

                            int id = i.leaderboard.id;
                            //leaderboard data:
                            //  combined rank = combined leaderboard rank
                            //  rank = top rank with selected settings
                            //         Note: this is only useful when modifiers=0.
                            //         For modifiers={} it returns the best rank with any combination of settings
                            //Both of these are unreliable and often return different values then the song leaderboards.
                            //#int rank = Math.Min((int)i.rank, 2000);
                            //Average all the scores better than the player, top 200 at worst, to limit the amount of API hammering.
                            if (modifier == "1") //nomods
                            {
                                rank = Math.Min((int)i.rank, 200);
                            }
                            else if (modifier == "2") //combined
                            {
                                rank = Math.Min((int)i.rank_combined, 200);
                            }
                            else // We don't know rank yet for spin or spiral and need to pull the whole leaderboard to figure that out.
                            {
                                rank = 0;
                            };
                            bool stop = false;
                            page = 0;
                            List<dynamic> map_leaderboard = new List<dynamic>();

                            //Highlight blue and gold notes
                            string bolts = "  ";
                            Console.ForegroundColor = ConsoleColor.White;
                            if (i.notes_hit == i.leaderboard.max_combo)
                            {
                                if (i.poor_hit_percent == 0)
                                {
                                    if (i.good_hit_percent == 0)
                                    {
                                        bolts = "!!";
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                    }
                                    else
                                    {
                                        bolts = "! ";
                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                    }
                                }
                            }
                            else
                            {
                                bolts = "x ";
                                Console.ForegroundColor = ConsoleColor.Red;
                            }

                            Console.WriteLine("");
                            if (modifier == "1" && rank > 0)
                            {
                                Console.Write("Map " + count + " of " + personal_leaderboard.Count + ": " + i.rank + bolts + " " + i.leaderboard.beatmap.artist + " - " + i.leaderboard.beatmap.title);
                            }
                            else if (modifier == "2" && rank > 0)
                            {
                                Console.Write("Map " + count + " of " + personal_leaderboard.Count + ": " + i.rank_combined + bolts + " (" + i.modifiers + dm + ") " + i.leaderboard.beatmap.artist + " - " + i.leaderboard.beatmap.title);
                            }
                            else //display a ? for spin, spiral, or other null responses
                            {
                                Console.Write("Map " + count + " of " + personal_leaderboard.Count + ": ?" + bolts + " (" + i.modifiers + dm + ") " + i.leaderboard.beatmap.artist + " - " + i.leaderboard.beatmap.title);
                            }


                            //**************************************************************************************************************************
                            //*************************************************  PARSING LEADERBOARD DATA  *********************************************
                            //**************************************************************************************************************************

                            //If not already the top score, how much room for improvement is there?
                            if (rank != 1)
                            {
                                // We already know ranks for no-mods and combined. But for spin/spiral, we need to figure it out.
                                // We start out assuming #1, and increment it for every higher score we find in the leaderboards.
                                if (modifier == "3" || modifier == "4")
                                {
                                    rank = 1;
                                }

                                do
                                {
                                    page++;
                                    Console.Write(".");

                                    
                                    string req = leaderboard_template;
                                    if (modifier == "1") //nomods
                                    {
                                        req = req.Replace("§modifiers§", "0");
                                    }
                                    else if(modifier =="2") //combined
                                    {
                                        req = req.Replace("§modifiers§", "-1");
                                    }
                                    else if (modifier == "3") //spin
                                    {
                                        //Brute-forcing the query for every possible spin mod combo.
                                        //This proved too large, so Halo and Vanish were excluded. But I do want to include common Halo & Vanish scores back in!
                                        req = combined_modifers_template;
                                        req = req.Replace("§modifiers§", "1,2,4,8,16385,16386,16388,16392,32769,32770,32772,32776,129,130,132,136,16513,16514,16516,16520,32897,32898,32900,32904,257,258,260,264,16641,16642,16644,16648,33025,33026,33028,33032,4097,4098,4100,4104,20481,20482,20484,20488,36865,36866,36868,36872,4225,4226,4228,4232,20609,20610,20612,20616,36993,36994,36996,37000,4353,4354,4356,4360,20737,20738,20740,20744,37121,37122,37124,37128,8193,8194,8196,8200,24577,24578,24580,24584,40961,40962,40964,40968,8321,8322,8324,8328,24705,24706,24708,24712,41089,41090,41092,41096,8449,8450,8452,8456,24833,24834,24836,24840,41217,41218,41220,41224,1025,1026,1028,1032,17409,17410,17412,17416,33793,33794,33796,33800,1153,1154,1156,1160,17537,17538,17540,17544,33921,33922,33924,33928,1281,1282,1284,1288,17665,17666,17668,17672,34049,34050,34052,34056,5121,5122,5124,5128,21505,21506,21508,21512,37889,37890,37892,37896,5249,5250,5252,5256,21633,21634,21636,21640,38017,38018,38020,38024,5377,5378,5380,5384,21761,21762,21764,21768,38145,38146,38148,38152,9217,9218,9220,9224,25601,25602,25604,25608,41985,41986,41988,41992,9345,9346,9348,9352,25729,25730,25732,25736,42113,42114,42116,42120,9473,9474,9476,9480,25857,25858,25860,25864,42241,42242,42244,42248,17,18,20,24,16401,16402,16404,16408,32785,32786,32788,32792,145,146,148,152,16529,16530,16532,16536,32913,32914,32916,32920,273,274,276,280,16657,16658,16660,16664,33041,33042,33044,33048,4113,4114,4116,4120,20497,20498,20500,20504,36881,36882,36884,36888,4241,4242,4244,4248,20625,20626,20628,20632,37009,37010,37012,37016,4369,4370,4372,4376,20753,20754,20756,20760,37137,37138,37140,37144,8209,8210,8212,8216,24593,24594,24596,24600,40977,40978,40980,40984,8337,8338,8340,8344,24721,24722,24724,24728,41105,41106,41108,41112,8465,8466,8468,8472,24849,24850,24852,24856,41233,41234,41236,41240,1041,1042,1044,1048,17425,17426,17428,17432,33809,33810,33812,33816,1169,1170,1172,1176,17553,17554,17556,17560,33937,33938,33940,33944,1297,1298,1300,1304,17681,17682,17684,17688,34065,34066,34068,34072,5137,5138,5140,5144,21521,21522,21524,21528,37905,37906,37908,37912,5265,5266,5268,5272,21649,21650,21652,21656,38033,38034,38036,38040,5393,5394,5396,5400,21777,21778,21780,21784,38161,38162,38164,38168,9233,9234,9236,9240,25617,25618,25620,25624,42001,42002,42004,42008,9361,9362,9364,9368,25745,25746,25748,25752,42129,42130,42132,42136,9489,9490,9492,9496,25873,25874,25876,25880,42257,42258,42260,42264,513,514,516,520,16897,16898,16900,16904,33281,33282,33284,33288,641,642,644,648,17025,17026,17028,17032,33409,33410,33412,33416,769,770,772,776,17153,17154,17156,17160,33537,33538,33540,33544,4609,4610,4612,4616,20993,20994,20996,21000,37377,37378,37380,37384,4737,4738,4740,4744,21121,21122,21124,21128,37505,37506,37508,37512,4865,4866,4868,4872,21249,21250,21252,21256,37633,37634,37636,37640,8705,8706,8708,8712,25089,25090,25092,25096,41473,41474,41476,41480,8833,8834,8836,8840,25217,25218,25220,25224,41601,41602,41604,41608,8961,8962,8964,8968,25345,25346,25348,25352,41729,41730,41732,41736,1537,1538,1540,1544,17921,17922,17924,17928,34305,34306,34308,34312,1665,1666,1668,1672,18049,18050,18052,18056,34433,34434,34436,34440,1793,1794,1796,1800,18177,18178,18180,18184,34561,34562,34564,34568,5633,5634,5636,5640,22017,22018,22020,22024,38401,38402,38404,38408,5761,5762,5764,5768,22145,22146,22148,22152,38529,38530,38532,38536,5889,5890,5892,5896,22273,22274,22276,22280,38657,38658,38660,38664,9729,9730,9732,9736,26113,26114,26116,26120,42497,42498,42500,42504,9857,9858,9860,9864,26241,26242,26244,26248,42625,42626,42628,42632,9985,9986,9988,9992,26369,26370,26372,26376,42753,42754,42756,42760,513,514,516,520,16897,16898,16900,16904,33281,33282,33284,33288,641,642,644,648,17025,17026,17028,17032,33409,33410,33412,33416,769,770,772,776,17153,17154,17156,17160,33537,33538,33540,33544,4609,4610,4612,4616,20993,20994,20996,21000,37377,37378,37380,37384,4737,4738,4740,4744,21121,21122,21124,21128,37505,37506,37508,37512,4865,4866,4868,4872,21249,21250,21252,21256,37633,37634,37636,37640,8705,8706,8708,8712,25089,25090,25092,25096,41473,41474,41476,41480,8833,8834,8836,8840,25217,25218,25220,25224,41601,41602,41604,41608,8961,8962,8964,8968,25345,25346,25348,25352,41729,41730,41732,41736,1537,1538,1540,1544,17921,17922,17924,17928,34305,34306,34308,34312,1665,1666,1668,1672,18049,18050,18052,18056,34433,34434,34436,34440,1793,1794,1796,1800,18177,18178,18180,18184,34561,34562,34564,34568,5633,5634,5636,5640,22017,22018,22020,22024,38401,38402,38404,38408,5761,5762,5764,5768,22145,22146,22148,22152,38529,38530,38532,38536,5889,5890,5892,5896,22273,22274,22276,22280,38657,38658,38660,38664,9729,9730,9732,9736,26113,26114,26116,26120,42497,42498,42500,42504,9857,9858,9860,9864,26241,26242,26244,26248,42625,42626,42628,42632,9985,9986,9988,9992,26369,26370,26372,26376,42753,42754,42756,42760,33,34,36,40,16417,16418,16420,16424,32801,32802,32804,32808,161,162,164,168,16545,16546,16548,16552,32929,32930,32932,32936,289,290,292,296,16673,16674,16676,16680,33057,33058,33060,33064,4129,4130,4132,4136,20513,20514,20516,20520,36897,36898,36900,36904,4257,4258,4260,4264,20641,20642,20644,20648,37025,37026,37028,37032,4385,4386,4388,4392,20769,20770,20772,20776,37153,37154,37156,37160,8225,8226,8228,8232,24609,24610,24612,24616,40993,40994,40996,41000,8353,8354,8356,8360,24737,24738,24740,24744,41121,41122,41124,41128,8481,8482,8484,8488,24865,24866,24868,24872,41249,41250,41252,41256,1057,1058,1060,1064,17441,17442,17444,17448,33825,33826,33828,33832,1185,1186,1188,1192,17569,17570,17572,17576,33953,33954,33956,33960,1313,1314,1316,1320,17697,17698,17700,17704,34081,34082,34084,34088,5153,5154,5156,5160,21537,21538,21540,21544,37921,37922,37924,37928,5281,5282,5284,5288,21665,21666,21668,21672,38049,38050,38052,38056,5409,5410,5412,5416,21793,21794,21796,21800,38177,38178,38180,38184,9249,9250,9252,9256,25633,25634,25636,25640,42017,42018,42020,42024,9377,9378,9380,9384,25761,25762,25764,25768,42145,42146,42148,42152,9505,9506,9508,9512,25889,25890,25892,25896,42273,42274,42276,42280,49,50,52,56,16433,16434,16436,16440,32817,32818,32820,32824,177,178,180,184,16561,16562,16564,16568,32945,32946,32948,32952,305,306,308,312,16689,16690,16692,16696,33073,33074,33076,33080,4145,4146,4148,4152,20529,20530,20532,20536,36913,36914,36916,36920,4273,4274,4276,4280,20657,20658,20660,20664,37041,37042,37044,37048,4401,4402,4404,4408,20785,20786,20788,20792,37169,37170,37172,37176,8241,8242,8244,8248,24625,24626,24628,24632,41009,41010,41012,41016,8369,8370,8372,8376,24753,24754,24756,24760,41137,41138,41140,41144,8497,8498,8500,8504,24881,24882,24884,24888,41265,41266,41268,41272,1073,1074,1076,1080,17457,17458,17460,17464,33841,33842,33844,33848,1201,1202,1204,1208,17585,17586,17588,17592,33969,33970,33972,33976,1329,1330,1332,1336,17713,17714,17716,17720,34097,34098,34100,34104,5169,5170,5172,5176,21553,21554,21556,21560,37937,37938,37940,37944,5297,5298,5300,5304,21681,21682,21684,21688,38065,38066,38068,38072,5425,5426,5428,5432,21809,21810,21812,21816,38193,38194,38196,38200,9265,9266,9268,9272,25649,25650,25652,25656,42033,42034,42036,42040,9393,9394,9396,9400,25777,25778,25780,25784,42161,42162,42164,42168,9521,9522,9524,9528,25905,25906,25908,25912,42289,42290,42292,42296,545,546,548,552,16929,16930,16932,16936,33313,33314,33316,33320,673,674,676,680,17057,17058,17060,17064,33441,33442,33444,33448,801,802,804,808,17185,17186,17188,17192,33569,33570,33572,33576,4641,4642,4644,4648,21025,21026,21028,21032,37409,37410,37412,37416,4769,4770,4772,4776,21153,21154,21156,21160,37537,37538,37540,37544,4897,4898,4900,4904,21281,21282,21284,21288,37665,37666,37668,37672,8737,8738,8740,8744,25121,25122,25124,25128,41505,41506,41508,41512,8865,8866,8868,8872,25249,25250,25252,25256,41633,41634,41636,41640,8993,8994,8996,9000,25377,25378,25380,25384,41761,41762,41764,41768,1569,1570,1572,1576,17953,17954,17956,17960,34337,34338,34340,34344,1697,1698,1700,1704,18081,18082,18084,18088,34465,34466,34468,34472,1825,1826,1828,1832,18209,18210,18212,18216,34593,34594,34596,34600,5665,5666,5668,5672,22049,22050,22052,22056,38433,38434,38436,38440,5793,5794,5796,5800,22177,22178,22180,22184,38561,38562,38564,38568,5921,5922,5924,5928,22305,22306,22308,22312,38689,38690,38692,38696,9761,9762,9764,9768,26145,26146,26148,26152,42529,42530,42532,42536,9889,9890,9892,9896,26273,26274,26276,26280,42657,42658,42660,42664,10017,10018,10020,10024,26401,26402,26404,26408,42785,42786,42788,42792,545,546,548,552,16929,16930,16932,16936,33313,33314,33316,33320,673,674,676,680,17057,17058,17060,17064,33441,33442,33444,33448,801,802,804,808,17185,17186,17188,17192,33569,33570,33572,33576,4641,4642,4644,4648,21025,21026,21028,21032,37409,37410,37412,37416,4769,4770,4772,4776,21153,21154,21156,21160,37537,37538,37540,37544,4897,4898,4900,4904,21281,21282,21284,21288,37665,37666,37668,37672,8737,8738,8740,8744,25121,25122,25124,25128,41505,41506,41508,41512,8865,8866,8868,8872,25249,25250,25252,25256,41633,41634,41636,41640,8993,8994,8996,9000,25377,25378,25380,25384,41761,41762,41764,41768,1569,1570,1572,1576,17953,17954,17956,17960,34337,34338,34340,34344,1697,1698,1700,1704,18081,18082,18084,18088,34465,34466,34468,34472,1825,1826,1828,1832,18209,18210,18212,18216,34593,34594,34596,34600,5665,5666,5668,5672,22049,22050,22052,22056,38433,38434,38436,38440,5793,5794,5796,5800,22177,22178,22180,22184,38561,38562,38564,38568,5921,5922,5924,5928,22305,22306,22308,22312,38689,38690,38692,38696,9761,9762,9764,9768,26145,26146,26148,26152,42529,42530,42532,42536,9889,9890,9892,9896,26273,26274,26276,26280,42657,42658,42660,42664,10017,10018,10020,10024,26401,26402,26404,26408,42785,42786,42788,42792");
                                    }
                                    else if (modifier == "4") //spiral
                                    {
                                        //Brute-forcing the query for every possible spiral mod combos 
                                        req = combined_modifers_template;
                                        req = req.Replace("§modifiers§", "131072,262144,524288,131200,262272,524416,131328,262400,524544,135168,266240,528384,135296,266368,528512,135424,266496,528640,139264,270336,532480,139392,270464,532608,139520,270592,532736,132096,263168,525312,132224,263296,525440,132352,263424,525568,136192,267264,529408,136320,267392,529536,136448,267520,529664,140288,271360,533504,140416,271488,533632,140544,271616,533760,131088,262160,524304,131216,262288,524432,131344,262416,524560,135184,266256,528400,135312,266384,528528,135440,266512,528656,139280,270352,532496,139408,270480,532624,139536,270608,532752,132112,263184,525328,132240,263312,525456,132368,263440,525584,136208,267280,529424,136336,267408,529552,136464,267536,529680,140304,271376,533520,140432,271504,533648,140560,271632,533776,131104,262176,524320,131232,262304,524448,131360,262432,524576,135200,266272,528416,135328,266400,528544,135456,266528,528672,139296,270368,532512,139424,270496,532640,139552,270624,532768,132128,263200,525344,132256,263328,525472,132384,263456,525600,136224,267296,529440,136352,267424,529568,136480,267552,529696,140320,271392,533536,140448,271520,533664,140576,271648,533792,131120,262192,524336,131248,262320,524464,131376,262448,524592,135216,266288,528432,135344,266416,528560,135472,266544,528688,139312,270384,532528,139440,270512,532656,139568,270640,532784,132144,263216,525360,132272,263344,525488,132400,263472,525616,136240,267312,529456,136368,267440,529584,136496,267568,529712,140336,271408,533552,140464,271536,533680,140592,271664,533808,131136,262208,524352,131264,262336,524480,131392,262464,524608,135232,266304,528448,135360,266432,528576,135488,266560,528704,139328,270400,532544,139456,270528,532672,139584,270656,532800,132160,263232,525376,132288,263360,525504,132416,263488,525632,136256,267328,529472,136384,267456,529600,136512,267584,529728,140352,271424,533568,140480,271552,533696,140608,271680,533824,131152,262224,524368,131280,262352,524496,131408,262480,524624,135248,266320,528464,135376,266448,528592,135504,266576,528720,139344,270416,532560,139472,270544,532688,139600,270672,532816,132176,263248,525392,132304,263376,525520,132432,263504,525648,136272,267344,529488,136400,267472,529616,136528,267600,529744,140368,271440,533584,140496,271568,533712,140624,271696,533840,131168,262240,524384,131296,262368,524512,131424,262496,524640,135264,266336,528480,135392,266464,528608,135520,266592,528736,139360,270432,532576,139488,270560,532704,139616,270688,532832,132192,263264,525408,132320,263392,525536,132448,263520,525664,136288,267360,529504,136416,267488,529632,136544,267616,529760,140384,271456,533600,140512,271584,533728,140640,271712,533856,131184,262256,524400,131312,262384,524528,131440,262512,524656,135280,266352,528496,135408,266480,528624,135536,266608,528752,139376,270448,532592,139504,270576,532720,139632,270704,532848,132208,263280,525424,132336,263408,525552,132464,263536,525680,136304,267376,529520,136432,267504,529648,136560,267632,529776,140400,271472,533616,140528,271600,533744,140656,271728,533872,133120,264192,526336,133248,264320,526464,133376,264448,526592,137216,268288,530432,137344,268416,530560,137472,268544,530688,141312,272384,534528,141440,272512,534656,141568,272640,534784,134144,265216,527360,134272,265344,527488,134400,265472,527616,138240,269312,531456,138368,269440,531584,138496,269568,531712,142336,273408,535552,142464,273536,535680,142592,273664,535808,133136,264208,526352,133264,264336,526480,133392,264464,526608,137232,268304,530448,137360,268432,530576,137488,268560,530704,141328,272400,534544,141456,272528,534672,141584,272656,534800,134160,265232,527376,134288,265360,527504,134416,265488,527632,138256,269328,531472,138384,269456,531600,138512,269584,531728,142352,273424,535568,142480,273552,535696,142608,273680,535824,133152,264224,526368,133280,264352,526496,133408,264480,526624,137248,268320,530464,137376,268448,530592,137504,268576,530720,141344,272416,534560,141472,272544,534688,141600,272672,534816,134176,265248,527392,134304,265376,527520,134432,265504,527648,138272,269344,531488,138400,269472,531616,138528,269600,531744,142368,273440,535584,142496,273568,535712,142624,273696,535840,133168,264240,526384,133296,264368,526512,133424,264496,526640,137264,268336,530480,137392,268464,530608,137520,268592,530736,141360,272432,534576,141488,272560,534704,141616,272688,534832,134192,265264,527408,134320,265392,527536,134448,265520,527664,138288,269360,531504,138416,269488,531632,138544,269616,531760,142384,273456,535600,142512,273584,535728,142640,273712,535856,133184,264256,526400,133312,264384,526528,133440,264512,526656,137280,268352,530496,137408,268480,530624,137536,268608,530752,141376,272448,534592,141504,272576,534720,141632,272704,534848,134208,265280,527424,134336,265408,527552,134464,265536,527680,138304,269376,531520,138432,269504,531648,138560,269632,531776,142400,273472,535616,142528,273600,535744,142656,273728,535872,133200,264272,526416,133328,264400,526544,133456,264528,526672,137296,268368,530512,137424,268496,530640,137552,268624,530768,141392,272464,534608,141520,272592,534736,141648,272720,534864,134224,265296,527440,134352,265424,527568,134480,265552,527696,138320,269392,531536,138448,269520,531664,138576,269648,531792,142416,273488,535632,142544,273616,535760,142672,273744,535888,133216,264288,526432,133344,264416,526560,133472,264544,526688,137312,268384,530528,137440,268512,530656,137568,268640,530784,141408,272480,534624,141536,272608,534752,141664,272736,534880,134240,265312,527456,134368,265440,527584,134496,265568,527712,138336,269408,531552,138464,269536,531680,138592,269664,531808,142432,273504,535648,142560,273632,535776,142688,273760,535904,133232,264304,526448,133360,264432,526576,133488,264560,526704,137328,268400,530544,137456,268528,530672,137584,268656,530800,141424,272496,534640,141552,272624,534768,141680,272752,534896,134256,265328,527472,134384,265456,527600,134512,265584,527728,138352,269424,531568,138480,269552,531696,138608,269680,531824,142448,273520,535664,142576,273648,535792,142704,273776,535920,131072,262144,524288,131200,262272,524416,131328,262400,524544,135168,266240,528384,135296,266368,528512,135424,266496,528640,139264,270336,532480,139392,270464,532608,139520,270592,532736,132096,263168,525312,132224,263296,525440,132352,263424,525568,136192,267264,529408,136320,267392,529536,136448,267520,529664,140288,271360,533504,140416,271488,533632,140544,271616,533760,131088,262160,524304,131216,262288,524432,131344,262416,524560,135184,266256,528400,135312,266384,528528,135440,266512,528656,139280,270352,532496,139408,270480,532624,139536,270608,532752,132112,263184,525328,132240,263312,525456,132368,263440,525584,136208,267280,529424,136336,267408,529552,136464,267536,529680,140304,271376,533520,140432,271504,533648,140560,271632,533776,131104,262176,524320,131232,262304,524448,131360,262432,524576,135200,266272,528416,135328,266400,528544,135456,266528,528672,139296,270368,532512,139424,270496,532640,139552,270624,532768,132128,263200,525344,132256,263328,525472,132384,263456,525600,136224,267296,529440,136352,267424,529568,136480,267552,529696,140320,271392,533536,140448,271520,533664,140576,271648,533792,131120,262192,524336,131248,262320,524464,131376,262448,524592,135216,266288,528432,135344,266416,528560,135472,266544,528688,139312,270384,532528,139440,270512,532656,139568,270640,532784,132144,263216,525360,132272,263344,525488,132400,263472,525616,136240,267312,529456,136368,267440,529584,136496,267568,529712,140336,271408,533552,140464,271536,533680,140592,271664,533808,131136,262208,524352,131264,262336,524480,131392,262464,524608,135232,266304,528448,135360,266432,528576,135488,266560,528704,139328,270400,532544,139456,270528,532672,139584,270656,532800,132160,263232,525376,132288,263360,525504,132416,263488,525632,136256,267328,529472,136384,267456,529600,136512,267584,529728,140352,271424,533568,140480,271552,533696,140608,271680,533824,131152,262224,524368,131280,262352,524496,131408,262480,524624,135248,266320,528464,135376,266448,528592,135504,266576,528720,139344,270416,532560,139472,270544,532688,139600,270672,532816,132176,263248,525392,132304,263376,525520,132432,263504,525648,136272,267344,529488,136400,267472,529616,136528,267600,529744,140368,271440,533584,140496,271568,533712,140624,271696,533840,131168,262240,524384,131296,262368,524512,131424,262496,524640,135264,266336,528480,135392,266464,528608,135520,266592,528736,139360,270432,532576,139488,270560,532704,139616,270688,532832,132192,263264,525408,132320,263392,525536,132448,263520,525664,136288,267360,529504,136416,267488,529632,136544,267616,529760,140384,271456,533600,140512,271584,533728,140640,271712,533856,131184,262256,524400,131312,262384,524528,131440,262512,524656,135280,266352,528496,135408,266480,528624,135536,266608,528752,139376,270448,532592,139504,270576,532720,139632,270704,532848,132208,263280,525424,132336,263408,525552,132464,263536,525680,136304,267376,529520,136432,267504,529648,136560,267632,529776,140400,271472,533616,140528,271600,533744,140656,271728,533872,133120,264192,526336,133248,264320,526464,133376,264448,526592,137216,268288,530432,137344,268416,530560,137472,268544,530688,141312,272384,534528,141440,272512,534656,141568,272640,534784,134144,265216,527360,134272,265344,527488,134400,265472,527616,138240,269312,531456,138368,269440,531584,138496,269568,531712,142336,273408,535552,142464,273536,535680,142592,273664,535808,133136,264208,526352,133264,264336,526480,133392,264464,526608,137232,268304,530448,137360,268432,530576,137488,268560,530704,141328,272400,534544,141456,272528,534672,141584,272656,534800,134160,265232,527376,134288,265360,527504,134416,265488,527632,138256,269328,531472,138384,269456,531600,138512,269584,531728,142352,273424,535568,142480,273552,535696,142608,273680,535824,133152,264224,526368,133280,264352,526496,133408,264480,526624,137248,268320,530464,137376,268448,530592,137504,268576,530720,141344,272416,534560,141472,272544,534688,141600,272672,534816,134176,265248,527392,134304,265376,527520,134432,265504,527648,138272,269344,531488,138400,269472,531616,138528,269600,531744,142368,273440,535584,142496,273568,535712,142624,273696,535840,133168,264240,526384,133296,264368,526512,133424,264496,526640,137264,268336,530480,137392,268464,530608,137520,268592,530736,141360,272432,534576,141488,272560,534704,141616,272688,534832,134192,265264,527408,134320,265392,527536,134448,265520,527664,138288,269360,531504,138416,269488,531632,138544,269616,531760,142384,273456,535600,142512,273584,535728,142640,273712,535856,133184,264256,526400,133312,264384,526528,133440,264512,526656,137280,268352,530496,137408,268480,530624,137536,268608,530752,141376,272448,534592,141504,272576,534720,141632,272704,534848,134208,265280,527424,134336,265408,527552,134464,265536,527680,138304,269376,531520,138432,269504,531648,138560,269632,531776,142400,273472,535616,142528,273600,535744,142656,273728,535872,133200,264272,526416,133328,264400,526544,133456,264528,526672,137296,268368,530512,137424,268496,530640,137552,268624,530768,141392,272464,534608,141520,272592,534736,141648,272720,534864,134224,265296,527440,134352,265424,527568,134480,265552,527696,138320,269392,531536,138448,269520,531664,138576,269648,531792,142416,273488,535632,142544,273616,535760,142672,273744,535888,133216,264288,526432,133344,264416,526560,133472,264544,526688,137312,268384,530528,137440,268512,530656,137568,268640,530784,141408,272480,534624,141536,272608,534752,141664,272736,534880,134240,265312,527456,134368,265440,527584,134496,265568,527712,138336,269408,531552,138464,269536,531680,138592,269664,531808,142432,273504,535648,142560,273632,535776,142688,273760,535904,133232,264304,526448,133360,264432,526576,133488,264560,526704,137328,268400,530544,137456,268528,530672,137584,268656,530800,141424,272496,534640,141552,272624,534768,141680,272752,534896,134256,265328,527472,134384,265456,527600,134512,265584,527728,138352,269424,531568,138480,269552,531696,138608,269680,531824,142448,273520,535664,142576,273648,535792,142704,273776,535920");
                                    }
                                    req = req.Replace("§page§", page.ToString());
                                    req = req.Replace("§id§", id.ToString());

                                    string resp = await client.DownloadStringTaskAsync(req);

                                    //We do not need to wrap resp for combined_modifiers_template, only leaderboard_template response
                                    if (modifier == "1" || modifier == "2")
                                    {
                                        resp = "{\"data\":" + resp + "}";
                                    }

                                    dynamic res_data = JObject.Parse(resp);

                                    //Display the top 10 leaderboard for each map.
                                    //Console.WriteLine(" id=" + id.ToString() + "   page=" + page.ToString());
                                    //Console.WriteLine(req);
                                    if (page == 1 && (modifier == "3" || modifier == "4" ))
                                    {
                                        int num = 0;
                                        Console.WriteLine();
                                        Console.WriteLine("Num:     Score:            Mods:      Name:");
                                        foreach (var j in res_data.data)
                                        {
                                            num++;
                                            
                                            //Highlight blue and gold notes in the leaderboard top 10s
                                            string jbolts = "  ";
                                            Console.ForegroundColor = ConsoleColor.White;
                                            if (j.notes_hit == j.max_combo)
                                            {
                                                if (j.poor_hit_percent == 0)
                                                {
                                                    if (j.good_hit_percent == 0)
                                                    {
                                                        jbolts = "!!";
                                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                                    }
                                                    else
                                                    {
                                                        jbolts = "! ";
                                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                jbolts = "x ";
                                                Console.ForegroundColor = ConsoleColor.Red;
                                            }
                                            jbolts = j.modified_score.ToString()+ jbolts;
                                            Console.WriteLine(num.ToString().PadRight(5, ' ') + "    " + jbolts.PadRight(14, ' ') + "    " + j.modifiers.ToString().PadRight(6, ' ') + "    " + j.name);
                                            Console.ForegroundColor = ConsoleColor.White;
                                            if (num > 9) { break; }
                                        }
                                    }

                                    //NEED TO FIX: Spin/Spiral sort for the player's best combined score only works properly for scores in the top-100, which is the maximum we can request in one API call.
                                    //To do this analysis right, we'd need to pull all the data into a single database file, like we do with personal leaderboards.
                                    //Sort the spin & spiral combined modifiers and eliminate duplicates 
                                    if (modifier == "3" || modifier == "4")
                                    {
                                        //reference: personal_leaderboard = personal_leaderboard.GroupBy(x => x.leaderboard.beatmap.id).Select(x => x.OrderByDescending(y => y.modified_score).Where(x => x.modified_score > 0).First()).ToList();
                                        //gave an error: res_data = res_data.OrderByDescending(x => x.modified_score).ToList();
                                        //IEnumerable<dynamic> data = res_data.data;
                                        //var spinspiral_data = data.GroupBy(x => x.profile.id).Select(x => x.OrderByDescending(y => y.modified_score).First()).ToList();

                                    }


                                    foreach (var j in res_data.data)
                                    {

                                        // The leaderboard contains rank and rank_combined:
                                        //     rank = the rank for that specific combination of modifiers
                                        //     rank_combined = the overall combined rank (modifiers=-1), but ONLY for the best score.  All other modifier combos with lesser scores have rank_combined=0.
                                        //Only a single j.rank is available in the song leaderboard.  However j.rank==j.rank_combined if modifiers=-1
                                        //We need to compare this to the appropriate rank or rank.combined, which are stored independently in the personal leaderboard
                                        //Console.WriteLine(j);


                                        //For no-mods and combined overall:
                                        if (modifier =="1" || modifier == "2")
                                        {
                                            if (j.rank < rank)
                                            {
                                                map_leaderboard.Add(j);
                                            }
                                            else
                                            {
                                                stop = true;
                                            }
                                        } else
                                        {
                                            // For spin and spiral
                                            if (j.modified_score > i.modified_score)
                                            {
                                                rank++;
                                                i.rank_combined = rank;
                                                map_leaderboard.Add(j);
                                            }
                                            else
                                            {
                                                i.rank_combined = rank;
                                                stop = true;
                                            }
                                        }

                                    }
                                    //Exit if page++ exceeds the number of pages, as could happen in some edge cases.
                                    if (page > 20)
                                    {
                                        Console.Write("+ ");
                                        Console.Write(req);
                                        stop = true;
                                    }

                                } while (!stop);

                            }

                            if (modifier == "3" || modifier == "4") //update rank average and stddev now that we know what they are.
                            {
                                average_rank_combined = personal_leaderboard.Average(x => (double)((JToken)x)["rank_combined"]);
                                stddev_rank_combined = StandardDeviation(personal_leaderboard.Select(x => (double)((JToken)x)["rank_combined"]));
                            }

                            //**********
                            //Check: Should we be using modified or base? 
                            //Modified_score will be the same regardless for modifiers=="0" for the filtered leaderboard, so should this be using modified_score for the average, too?
                            //Also, this is where we might do statistical analysis to look at the upper tail mean & standard deviation to develop a Z metric and a better ROI calculation.
                            int average_score = i.modified_score;
                            if (map_leaderboard.Count() > 0)
                            {
                                // The leaderboards api uses baseScore & modifiedScore while the score api returns base_score and modified_score
                                if (modifier == "1")
                                {
                                    average_score = (int)map_leaderboard.Average(x => x.modifiedScore);
                                    //average_score = (int)map_leaderboard.Average(x => x.baseScore);
                                }
                                else if (modifier == "2") 
                                { 
                                    average_score = (int)map_leaderboard.Average(x => x.modifiedScore);
                                } else 
                                { 
                                    average_score = (int)map_leaderboard.Average(x => x.modified_score);
                                }
                            }

                            //Calculate the new ROI metrics and save the results
                            //#Use rank for unmodified, or rank_combined if modified
                            if (modifiers == "0")
                            {
                                results.Add(new
                                {
                                    rank = i.rank,
                                    //ratio = 1-(float)i.modified_score / average_score,
                                    ratio = (float)i.rank * (1 - ((float)i.modified_score / average_score)),
                                    title = i.leaderboard.beatmap.title + " - " + i.leaderboard.beatmap.artist,
                                    bolts = bolts
                                });
                            }
                            else
                            {
                                results.Add(new
                                {
                                    rank = i.rank_combined,
                                    //ratio = (float)i.modified_score / average_score,
                                    ratio = (float)i.rank_combined * (1 - ((float)i.modified_score / average_score)),
                                    title = i.leaderboard.beatmap.title + " - " + i.leaderboard.beatmap.artist,
                                    bolts = bolts
                                });

                            }
                        }

                        // ***********************************************************
                        // Display an ordered list of maps, from best rank to worse
                        // ***********************************************************
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("");
                        Console.WriteLine("");
                        Console.Write("\u001b[4m"); // Underline
                        Console.Write(SummaryHeading + ", Played Maps:");
                        Console.WriteLine("\u001b[0m"); // Reset
                        Console.WriteLine("Results (best at the top): ");
                        results = results.OrderBy(x => x.rank).ToList();
                        foreach (var res in results)
                        {
                            //Highlight blue and gold notes
                            string rank_bolts = res.rank.ToString() + res.bolts;
                            Console.ForegroundColor = ConsoleColor.White;
                            if (res.bolts == "!!")
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                            }
                            else if (res.bolts == "! ")
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                            }
                            else if (res.bolts == "x ")
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                            }

                            Console.WriteLine("rank: " + rank_bolts.PadRight(4, ' ') + " ratio: " + res.ratio.ToString("n3") + " " + res.title);
                        }

                        // ***********************************************************
                        // Display an ordered list of maps, from highest to lower potential gain
                        // ***********************************************************
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("");
                        Console.WriteLine("");
                        Console.Write("\u001b[4m"); // Underline
                        Console.Write(SummaryHeading + ", Played Maps:");
                        Console.WriteLine("\u001b[0m"); // Reset
                        Console.WriteLine("Results (most opportunity for improvement at the top): ");
                        //results = results.OrderByDescending(x => x.rank).ToList();
                        results = results.OrderBy(x => x.ratio).ToList();
                        results = results.OrderByDescending(x => x.ratio).ToList();
                        foreach (var res in results)
                        {
                            //Highlight blue and gold notes
                            string rank_bolts = res.rank.ToString() + res.bolts;
                            Console.ForegroundColor = ConsoleColor.White;
                            if (res.bolts == "!!")
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                            }
                            else if (res.bolts == "! ")
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                            }
                            else if (res.bolts == "x ")
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                            }

                            Console.WriteLine("rank: " + rank_bolts.PadRight(4, ' ') + " ratio: " + res.ratio.ToString("n3") + " " + res.title);
                        }
                    }
                    else if(modifier=="1"||modifier=="2") //if not played
                    {
                        // ***********************************************************
                        // Analyze unplayed maps for the best opportunities
                        // ***********************************************************
                        page = 0;
                        pages = 0;

                        do
                        {
                            page++;
                            Console.WriteLine("All Leaderboard page: " + page + " of " + pages);
                            string req = all_leaderboards_template;
                            req = req.Replace("§page§", page.ToString());
                            req = req.Replace("§difficulty§", difficulty.ToString());
                            req = req.Replace("§mode§", mode.ToString());
                            //Console.WriteLine(req);

                            string resp = await client.DownloadStringTaskAsync(req);
                            dynamic res_data = JObject.Parse(resp);

                            all_leaderboards.AddRange(res_data.data);
                            page = res_data.page;
                            pages = res_data.pageCount;
                        } while (page < pages);

                        //NOTE: ADD SUPPORT FOR SPIN/SPIRAL.
                        //We need to reformat some of the modifiers to work with the leaderboards. -1 returns the combined leaderboard.

                        int count = 0;
                        List<dynamic> results = new List<dynamic>();

                        all_leaderboards = all_leaderboards.GroupBy(x => x.beatmap.id).Select(x => x.OrderByDescending(y => y.modified_score).First()).ToList();
                        var unplayed_leaderboards = all_leaderboards.Where(x => !personal_leaderboard.Any(y => y.leaderboard.beatmap.id == x.beatmap.id)).ToList();

                        // NOTE: SKIP ERRONEOUS LEADERBOARDS. Known bad  leaderboard.id = 7830 & 9668.
                        // This is a lazy hack, but these are the only two known bad leaderboards, and other methods risk excluding valid leaderboards
                        unplayed_leaderboards = unplayed_leaderboards.Where(x => (x.id != 7830 && x.id != 9668)).ToList();

                        //Determine how many pages deep to analyze based upon the player's rank. Go appx 30% beyond their average rank. But not more than 500 results per map.
                        int pages_deep = (int)average_rank / 7 + 1;
                        if (pages_deep > 49)
                        {
                            pages_deep = 49;
                        }
                        Console.WriteLine();
                        Console.WriteLine("Analyzing top " + ((pages_deep + 1) * 10) + " results for each unplayed map.");

                        foreach (var leaderboard in unplayed_leaderboards)
                        {
                            count++;
                            Console.Write("Map " + count + " of " + unplayed_leaderboards.Count);

                            bool stop = false;
                            page = 0;

                            do
                            {
                                page++;

                                Console.Write(".");

                                string req = leaderboard_template;
                                req = req.Replace("§page§", page.ToString());
                                req = req.Replace("§id§", leaderboard.id.ToString());
                                req = req.Replace("§modifiers§", modifiers);
                                //#req = req.Replace("§difficulty§", difficulty.ToString());
                                //#req = req.Replace("§mode§", mode.ToString());

                                string resp = await client.DownloadStringTaskAsync(req);
                                resp = "{\"data\":" + resp + "}";

                                dynamic res_data = JObject.Parse(resp);
                                foreach (var j in res_data.data)
                                {
                                    double accuracy = double.Parse((string)j.poorHitPercent) * 0.25 + double.Parse((string)j.goodHitPercent) * 0.5 + double.Parse((string)j.perfectHitPercent);

                                    if (accuracy <= average_accuracy)
                                    {
                                        results.Add(new
                                        {
                                            rank = j.rank,
                                            title = leaderboard.beatmap.title + " - " + leaderboard.beatmap.artist
                                        });
                                        stop = true;
                                        break;
                                    }
                                }
                                if (page > pages_deep && stop == false)
                                {
                                    results.Add(new
                                    {
                                        rank = (pages_deep + 1) * 10,
                                        title = leaderboard.beatmap.title + " - " + leaderboard.beatmap.artist
                                    }); ;
                                    stop = true;
                                }

                            } while (!stop);
                            Console.WriteLine("");
                        } 

                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("");
                        Console.Write("\u001b[4m"); // Underline
                        Console.Write(SummaryHeading + ", Estimated rank for unplayed maps:");
                        Console.WriteLine("\u001b[0m"); // Reset
                        results = results.OrderBy(x => (int)x.rank).ToList();
                        foreach (var res in results)
                        {
                            if (res.rank > average_rank)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.White;
                            }

                            // Indicate if the ranking exceeded our search criteria
                            string rank_display = res.rank.ToString();
                            if (res.rank == ((pages_deep + 1) * 10))
                            {
                                rank_display = res.rank.ToString() + "+";
                                Console.ForegroundColor = ConsoleColor.DarkRed;
                            }
                            Console.WriteLine("rank: " + rank_display.PadRight(5, ' ') + " " + res.title);
                            // Console.WriteLine("rank: " + res.rank.ToString().PadRight(5, ' ') + " " + res.title);
                        }
                    } else //they selected unplayed maps + spin or spiral
                    {
                        Console.WriteLine("Spin/Spiral is not yet supported in this mode, only No Mods and Combined Overall.");
                        Console.WriteLine();

                    } //closing else if not played
                } //closing if !=average
            } //closing using webclient
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("");
            Console.Write("\u001b[4m"); // Underline
            Console.Write(SummaryHeading + ", Statistics:");
            Console.WriteLine("\u001b[0m"); // Reset
            Console.WriteLine($"Average Poor:          {average_poor:n4} (σ {stddev_poor:n4})");
            Console.WriteLine($"Average Good:          {average_good:n4} (σ {stddev_good:n4})");
            Console.WriteLine($"Average Perfect:       {average_perfect:n4} (σ {stddev_perfect:n4})");
            Console.WriteLine($"Average Accuracy:      {average_accuracy:n4} (σ {stddev_accuracy:n4})");

            if (modifier == "1") //Display modifier-specific rank
            {
                Console.WriteLine($"Average Rank:          {average_rank:n2}   (σ {stddev_rank:n2})");
                //Console.WriteLine("Average Rank:         " + average_rank.ToString("n2") + " (σ " + stddev_rank.ToString("n2") + ")");
            }
            else //Display combined rank (overall, overall spin, or overall spiral)
            {
                Console.WriteLine($"Average Combined Rank: {average_rank:n2}   (σ {stddev_rank_combined:n2})");
            }
            WriteFile("scores/" + userid + "" + difficulty + "" + mode + "" + modifiers + ".csv");

            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("Press Enter to exit...");
            Console.ReadLine();
        }

        static void WriteFile(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (dir != "" && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            }
            if (!System.IO.File.Exists(path))
            {
                using (StreamWriter sw = System.IO.File.CreateText(path))
                {
                    sw.WriteLine("Date, Accuracy, Perfect, Good, Poor, Rank");
                }
            }
            using (StreamWriter sw = System.IO.File.AppendText(path))
            {
                sw.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ", " + average_accuracy + ", " + average_perfect + ", " + average_good + ", " + average_poor + ", " + average_rank);
            }
        }

        // Helper function StandardDeviation returns the standard deviation of a value.
        static double StandardDeviation(IEnumerable<double> values)
        {
            double avg = values.Average();
            double sumSquares = values.Sum(v => Math.Pow(v - avg, 2));
            return Math.Sqrt(sumSquares / values.Count());
        }
    }
}
