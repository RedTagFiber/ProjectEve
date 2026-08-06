using ProjectEve.Characters.Base;
using ProjectEve.Characters.Characters;
using ProjectEve.Characters.NPCs;
using System;
using System.Collections.Generic;

namespace ProjectEve.Worlds.SmallTown
{
    public class SmallTownWorld
    {
        public string Name { get; private set; } = string.Empty;
        public Town Town { get; private set; } = new Town();

        public void Generate(int population)
        {
            Town = TownGenerator.GenerateTown(population);
            Name = Town.Name ?? string.Empty;

        }
    }

    public class Town
    {
        public string Name { get; set; } = string.Empty;
        public int Population { get; set; }

        public List<SimCharacter> Residents { get; set; } = new();
        public List<TownStreet> Streets { get; set; } = new();
        public List<TownBuilding> Buildings { get; set; } = new();
        public List<TownEvent> Events { get; set; } = new();

    }

    public class TownStreet
    {
        public string Name { get; set; } = string.Empty;
        public List<TownBuilding> Buildings { get; set; } = new();
    }

    public class TownBuilding
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string JobType { get; set; } = string.Empty;
    }

    public class TownEvent
    {
        public string Name { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DayOfWeek Day { get; set; }
    }

    public static class TownGenerator
    {
        private static readonly Random rng = new();

        public static Town GenerateTown(int population)
        {
            var town = new Town
            {
                Name = GenerateTownName(),
                Population = population
            };

            town.Streets = GenerateStreets();
            town.Buildings = GenerateBuildings(town.Streets);
            town.Events = GenerateEvents(town);
            town.Residents = GenerateResidents(population, town);

            return town;
        }

        private static string GenerateTownName()
        {
            string[] prefixes =
            {
                "Campbell", "Rushcreek", "Zanesfield", "Indian Lake", "Madriver",
                "Logan", "Blue Jacket", "McArthur", "Sandusky", "Lakeview",
                "Huntsville", "West Liberty", "DeGraff", "Quincy", "Pickrelltown",
                "Bokes Creek", "Northwood", "Sloan", "Harmon", "High Point",
                "Orchard Island", "Long Island", "Russells Point", "Five Points"
            };

            string[] suffixes =
            {
                "Ridge", "Crossing", "Hollow", "Run", "Fork",
                "Corners", "Station", "Landing", "Heights", "Valley",
                "Springs", "Point", "Harbor", "Shore", "Meadows",
                "Woods", "Hills", "Prairie", "Fields", "Bluffs",
                "Village", "Center", "Park", "Terrace", "Grove"
            };

            return $"{prefixes[rng.Next(prefixes.Length)]} {suffixes[rng.Next(suffixes.Length)]}";
        }

        private static List<TownStreet> GenerateStreets()
        {
            string[] streetNames =
            {
                "Main Street", "Sandusky Avenue", "Madriver Street", "Detroit Street",
                "Rush Avenue", "Lake Avenue", "Auburn Avenue", "Ludlow Road",
                "County Road 32", "Township Road 179", "McArthur Street",
                "High Point Road", "Orchard Island Road", "Huntsville Road",
                "West Liberty Street", "Zanesfield Road", "Campbell Street",
                "Five Points Road"
            };

            var streets = new List<TownStreet>();
            foreach (var name in streetNames)
                streets.Add(new TownStreet { Name = name });

            return streets;
        }

        private static List<TownBuilding> GenerateBuildings(List<TownStreet> streets)
        {
            string[] buildingTypes =
            {
                "House", "Farmhouse", "Barn", "Grain Silo", "Gas Station",
                "Dollar General", "Auto Shop", "Coffee Shop", "Diner",
                "Bar & Grill", "School", "Hospital", "Library",
                "Police Station", "Fire Station", "Hardware Store",
                "Factory", "Warehouse", "Marina", "Bait Shop",
                "Ski Lodge", "Campground Office", "Lakeside Cabin"
            };

            string[] jobTypes =
            {
                "Unemployed", "Farmer", "Ranch Hand", "Factory Worker",
                "Honda Plant Worker", "Machine Operator", "Welder",
                "Forklift Driver", "Assembly Line Worker", "Quality Control Tech",
                "Dollar General Clerk", "Gas Station Attendant", "Cashier",
                "Waiter", "Cook", "Bartender", "Barber", "Mechanic",
                "Teacher", "Nurse", "EMT", "Police Officer", "Firefighter",
                "Office Worker", "Campground Staff", "Marina Worker",
                "Ski Lodge Employee"
            };

            var buildings = new List<TownBuilding>();

            foreach (var street in streets)
            {
                int count = rng.Next(4, 10);

                for (int i = 0; i < count; i++)
                {
                    string type = buildingTypes[rng.Next(buildingTypes.Length)];
                    string job = AssignJobForBuildingType(type, jobTypes);

                    var building = new TownBuilding
                    {
                        Name = $"{type} #{rng.Next(1, 999)}",
                        Type = type,
                        Street = street.Name,
                        JobType = job
                    };

                    buildings.Add(building);
                    street.Buildings.Add(building);
                }
            }

            return buildings;
        }

        private static string AssignJobForBuildingType(string type, string[] jobTypes)
        {
            return type switch
            {
                "Farmhouse" or "Barn" or "Grain Silo" => "Farmer",
                "Factory" => "Factory Worker",
                "Dollar General" => "Dollar General Clerk",
                "Gas Station" => "Gas Station Attendant",
                "Auto Shop" => "Mechanic",
                "Coffee Shop" or "Diner" or "Bar & Grill" => "Cook",
                "School" => "Teacher",
                "Hospital" => "Nurse",
                "Police Station" => "Police Officer",
                "Fire Station" => "Firefighter",
                "Marina" => "Marina Worker",
                "Ski Lodge" => "Ski Lodge Employee",
                "Campground Office" => "Campground Staff",
                _ => jobTypes[rng.Next(jobTypes.Length)]
            };
        }

        private static List<TownEvent> GenerateEvents(Town town)
        {
            string[] eventNames =
            {
                "Logan County Fair", "Indian Lake Fireworks", "Mad River Mountain Winterfest",
                "Downtown Music Night", "Farmers Market", "High School Football Game",
                "Holiday Parade", "Lake Cleanup Day", "Ice Fishing Derby",
                "Orchard Island Cookout", "Five Points Flea Market",

                "Ohio State Fair", "Columbus Arts Festival", "Red, White & Boom",
                "Ohio Stadium Game Day", "Columbus Crew Match", "Nationwide Arena Concert",
                "Scioto Mile River Festival", "COSI Science Expo",

                "Cincinnati Music Festival", "Fountain Square Live",
                "Reds Game at Great American Ball Park", "Bengals Game at Paycor Stadium",
                "Cincinnati Zoo Lights", "Riverfront Fireworks",
                "Kings Island Summer Blast", "Kings Island Haunt Night",

                "Rock & Roll Hall of Fame Concert", "Cleveland Air Show",
                "Guardians Game at Progressive Field", "Browns Game at Cleveland Stadium",
                "Cleveland Orchestra Night", "Cuyahoga River Festival",
                "Cedar Point Opening Day", "Cedar Point Halloweekends",

                "Dayton Air Force Museum Expo", "Dayton Dragons Baseball Night",
                "Oregon District Music Crawl", "Dayton Art Institute Gala"
            };

            string[] eventDescriptions =
            {
                "Local families gather for rides, food, and livestock shows.",
                "Fireworks over the lake with boats and cabins lit up.",
                "Skiing, snowboarding, and lodge parties at Mad River Mountain.",
                "Live bands play downtown with food trucks and small-town vibes.",
                "Local farmers sell produce, crafts, and homemade goods.",
                "Friday night lights with the whole town in the stands.",
                "Floats, marching bands, and kids lining the streets.",
                "Volunteers clean the shoreline and docks around the lake.",
                "Fishermen compete for biggest catch on the frozen lake.",
                "Neighbors grill, play music, and hang out by the water.",
                "Vendors set up tables with antiques, tools, and oddities.",

                "Massive crowds gather for Ohio's biggest fair.",
                "Artists line the riverfront with booths and live performances.",
                "Ohio's largest fireworks show lights up downtown Columbus.",
                "The Horseshoe fills with cheering Buckeye fans.",
                "Soccer fans pack Lower.com Field for a Crew match.",
                "Nationwide Arena hosts a major touring artist.",
                "Families gather along the Scioto Mile for food and music.",
                "COSI hosts hands-on science exhibits and demonstrations.",

                "National artists perform at one of the Midwest's biggest festivals.",
                "Live music fills Fountain Square every weekend.",
                "Fans cheer the Reds at Great American Ball Park.",
                "The Bengals take the field in downtown Cincinnati.",
                "The Cincinnati Zoo glows with holiday lights.",
                "Fireworks explode over the Ohio River.",
                "Kings Island hosts a massive summer celebration.",
                "Haunt actors roam Kings Island during Halloween season.",

                "Rock legends perform at the Hall of Fame.",
                "Jets roar over Lake Erie during the air show.",
                "Fans pack Progressive Field for a Guardians game.",
                "The Browns take the field in downtown Cleveland.",
                "The Cleveland Orchestra performs at Severance Hall.",
                "Boats gather for the Cuyahoga River celebration.",
                "Roller coasters roar as Cedar Point opens for the season.",
                "Monsters roam Cedar Point during Halloweekends.",

                "The Air Force Museum hosts a massive aviation expo.",
                "Fans cheer the Dayton Dragons at the ballpark.",
                "Bands fill the Oregon District with nightlife energy.",
                "The Art Institute hosts a formal gala event."
            };

            var events = new List<TownEvent>();

            for (int i = 0; i < eventNames.Length; i++)
            {
                events.Add(new TownEvent
                {
                    Name = eventNames[i],
                    Description = eventDescriptions[rng.Next(eventDescriptions.Length)],
                    Location = PickEventLocation(town) ?? "Unknown Location",

                    Day = (DayOfWeek)rng.Next(0, 7)
                });
            }

            return events;
        }

        private static string PickEventLocation(Town town)
        {
            string[] bigLocations =
            {
                "Ohio Stadium", "Nationwide Arena", "Lower.com Field", "COSI",
                "Scioto Mile", "Kings Island", "Great American Ball Park",
                "Paycor Stadium", "Cincinnati Zoo", "Rock & Roll Hall of Fame",
                "Progressive Field", "Cleveland Stadium", "Severance Hall",
                "Cedar Point", "Air Force Museum", "Dayton Dragons Ballpark",
                "Oregon District"
            };

            if (rng.NextDouble() < 0.5 && town.Buildings.Count > 0)
            {
                var b = town.Buildings[rng.Next(town.Buildings.Count)];
                return $"{b.Name} on {b.Street}";
            }

            return bigLocations[rng.Next(bigLocations.Length)];
        }

        private static List<SimCharacter> GenerateResidents(int population, Town town)
        {
            var residents = new List<SimCharacter>();

            var houses = town.Buildings.FindAll(b =>
                b.Type == "House" || b.Type == "Farmhouse" || b.Type == "Lakeside Cabin");

            if (houses.Count == 0)
                houses.Add(new TownBuilding { Name = "Temporary Housing", Street = "Main Street", Type = "House" });

            for (int i = 0; i < population; i++)
            {
                SimCharacter? npc = CharacterFactory.LoadCharacter(i + 1);


                if (npc != null)
                {
                    var home = houses[rng.Next(houses.Count)];
                    npc.Location = home.Name;

                    var jobBuilding = town.Buildings[rng.Next(town.Buildings.Count)];
                    npc.Occupation = jobBuilding.JobType ?? "Unemployed";


                    ApplyLocalTraits(npc);

                    residents.Add(npc);
                }
            }

            return residents;
        }

        private static void ApplyLocalTraits(SimCharacter npc)
        {
            npc.PersonalityTags ??= new List<string>();

            string[] localTraits =
            {
                "Friendly", "Hardworking", "Outdoorsy", "SmallTownPride",
                "FamilyOriented", "ChurchGoing", "Quiet", "Reserved",
                "Loyal", "CommunityFocused", "Practical", "DownToEarth"
            };

            int count = rng.Next(2, 5);
            for (int i = 0; i < count; i++)
            {
                var trait = localTraits[rng.Next(localTraits.Length)];
                if (!npc.PersonalityTags.Contains(trait))
                    npc.PersonalityTags.Add(trait);
            }
        }
    }
}
