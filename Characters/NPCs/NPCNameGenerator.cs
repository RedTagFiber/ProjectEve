using ProjectEve.Traits;
using System;
using System.Collections.Generic;

namespace ProjectEve.Characters.NPCs
{
    public static class NPCNameGenerator
    {
        private static readonly Random Rng = new();

        public static string GenerateFullName(string gender, string race, NpcTraits? traits = null)
        {
            string first = GenerateFirstName(gender);
            string middle = GenerateMiddleName(gender);
            string last = GenerateLastName(race);
            string nickname = GenerateNickname(gender, traits);

            int roll = Rng.Next(0, 100);
            if (roll < 18) return $"{first} \"{nickname}\" {last}";
            if (roll < 55) return $"{first} {middle} {last}";
            return $"{first} {last}";
        }

        public static (string First, string Middle, string Last, string Nickname) GenerateNameParts(
            string gender, string race, NpcTraits? traits = null)
            => (GenerateFirstName(gender), GenerateMiddleName(gender), GenerateLastName(race), GenerateNickname(gender, traits));

        public static string GenerateFirstName(string gender)
            => IsMale(gender) ? Pick(MaleFirst) : Pick(FemaleFirst);

        public static string GenerateMiddleName(string gender)
            => IsMale(gender) ? Pick(MaleMiddle) : Pick(FemaleMiddle);

        public static string GenerateLastName(string race)
        {
            var pool = race?.Trim() switch
            {
                "African" => AfricanLast,
                "Latino" => LatinoLast,
                "Asian" => AsianLast,
                "Middle Eastern" => MiddleEasternLast,
                "Pacific Islander" => PacificLast,
                "Mixed" => MixedLast,
                _ => EuropeanLast
            };
            string last = Pick(pool);
            if (pool.Length > 2 && Rng.Next(0, 100) < 3)
            {
                string a = Pick(pool), b = Pick(pool);
                if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                    return $"{a}-{b}";
            }
            return last;
        }

        // =====================================================================
        // MALE FIRST (~280)
        // =====================================================================
        private static readonly string[] MaleFirst =
        {
            "James","John","Michael","Robert","David","William","Richard","Joseph","Thomas","Charles",
            "Daniel","Matthew","Anthony","Mark","Donald","Steven","Paul","Andrew","Joshua","Kenneth",
            "Kevin","Brian","George","Timothy","Ronald","Edward","Jason","Jeffrey","Ryan","Jacob",
            "Gary","Nicholas","Eric","Jonathan","Stephen","Larry","Justin","Scott","Brandon","Benjamin",
            "Samuel","Raymond","Gregory","Frank","Alexander","Patrick","Jack","Dennis","Jerry","Tyler",
            "Aaron","Jose","Henry","Adam","Douglas","Nathan","Peter","Zachary","Kyle","Walter",
            "Harold","Jeremy","Ethan","Carl","Keith","Roger","Gerald","Christian","Terry","Sean",
            "Austin","Arthur","Lawrence","Jesse","Dylan","Bryan","Joel","Jordan","Billy","Bruce",
            "Gabriel","Logan","Alan","Juan","Wayne","Roy","Ralph","Randy","Eugene","Vincent",
            "Russell","Louis","Philip","Bobby","Johnny","Bradley","Dale","Howard","Victor","Craig",
            "Liam","Noah","Oliver","Elijah","Lucas","Mason","Jayden","Aiden","Caleb","Owen",
            "Hunter","Connor","Carson","Colton","Cooper","Parker","Gavin","Chase","Brody","Wyatt",
            "Hudson","Easton","Jaxon","Grayson","Lincoln","Nolan","Asher","Sawyer","Bentley","Maverick",
            "Kai","Ryder","Knox","Beckett","Silas","Rowan","Finn","Theo","Leo","Milo",
            "Ezra","Luca","Jasper","Felix","Hugo","Arlo","Jude","Atlas","Bodhi","Cruz",
            "Declan","Emmett","Gideon","Harrison","Ian","Jett","Kaden","Landon","Max","Nash",
            "Oscar","Paxton","Quinton","Rhett","Sebastian","Tristan","Uriel","Vaughn","Wesley","Xavier",
            "Zane","Cole","Grant","Blake","Tanner","Travis","Cody","Shane","Derek","Brett",
            "Spencer","Mitchell","Garrett","Clint","Wade","Hank","Burt","Clay","Duke","Rex",
            "Troy","Marcus","Dustin","Chad","Kirk","Todd","Glenn","Vernon","Earl","Floyd",
            "Merle","Calvin","Curtis","Dean","Gene","Hal","Ike","Jed","Kent","Lyle",
            "Mack","Ned","Otis","Pete","Quincy","Reed","Sam","Ted","Vern","Warren",
            "Abel","Adrian","Barrett","Beau","Brady","Brant","Brent","Bryce","Byron","Cade",
            "Cameron","Clark","Cliff","Clint","Collin","Corbin","Curtis","Dane","Darren","Davis",
            "Dawson","Derrick","Desmond","Dexter","Dominic","Donovan","Drake","Drew","Dwayne","Edgar",
            "Edwin","Elliot","Ellis","Elmer","Enrique","Ernest","Esteban","Everett","Fabian","Fernando",
            "Fletcher","Francis","Frederick","Gage","Garrett","Garrison","Gilbert","Gordon","Graham","Grant",
            "Griffin","Harold","Harvey","Hayden","Hector","Holden","Houston","Hugh","Ivan","Jared",
            "Jarvis","Javier","Jeffrey","Jeremiah","Joel","Jonas","Julian","Kaleb","Keith","Kelvin",
            "Kendrick","Kenneth","Kevin","Lance","Lawrence","Leon","Leonard","Leroy","Lewis","Lloyd",
            "Louis","Malcolm","Manuel","Marco","Mario","Marshall","Martin","Marvin","Mason","Matteo",
            "Maxwell","Melvin","Micah","Miguel","Miles","Mitchell","Morgan","Nathaniel","Neil","Nelson",
            "Nicholas","Nigel","Norman","Oliver","Omar","Orlando","Otto","Pablo","Patrick","Paul",
            "Pedro","Percy","Perry","Philip","Preston","Quentin","Rafael","Ralph","Ramon","Randall",
            "Raul","Raymond","Reginald","Ricardo","Richard","Riley","Roberto","Rodney","Roger","Roland",
            "Roman","Ronald","Rory","Ross","Ruben","Russell","Salvador","Samuel","Santiago","Scott",
            "Sean","Sergio","Seth","Shane","Shawn","Sidney","Simon","Spencer","Stanley","Stephen",
            "Sterling","Steve","Stuart","Tanner","Terrance","Terry","Theodore","Thomas","Timothy","Toby",
            "Todd","Tom","Tony","Trevor","Trent","Trevor","Tyler","Tyrone","Vernon","Victor",
            "Vincent","Wade","Wallace","Walter","Warren","Wayne","Wesley","Will","William","Wyatt","Zach"
        };

        // =====================================================================
        // FEMALE FIRST (~280)
        // =====================================================================
        private static readonly string[] FemaleFirst =
        {
            "Mary","Patricia","Jennifer","Linda","Elizabeth","Corrie","Barbara","Susan","Jessica","Sarah","Karen",
            "Nancy","Lisa","Betty","Margaret","Sandra","Ashley","Kimberly","Emily","Donna","Michelle",
            "Dorothy","Carol","Amanda","Melissa","Deborah","Stephanie","Rebecca","Sharon","Laura","Cynthia",
            "Kathleen","Amy","Angela","Shirley","Anna","Brenda","Pamela","Emma","Nicole","Helen",
            "Samantha","Katherine","Christine","Debra","Rachel","Carolyn","Janet","Catherine","Maria","Heather",
            "Diane","Ruth","Julie","Olivia","Joyce","Virginia","Victoria","Kelly","Lauren","Christina",
            "Joan","Evelyn","Judith","Megan","Andrea","Cheryl","Hannah","Jacqueline","Martha","Gloria",
            "Teresa","Ann","Sara","Madison","Frances","Kathryn","Janice","Jean","Abigail","Alice",
            "Judy","Sophia","Grace","Denise","Amber","Doris","Marilyn","Danielle","Beverly","Isabella",
            "Theresa","Diana","Natalie","Brittany","Charlotte","Kayla","Alexis","Lori","Olivia","Emma",
            "Ava","Sophia","Isabella","Mia","Amelia","Harper","Evelyn","Abigail","Emily","Ella",
            "Camila","Luna","Sofia","Avery","Mila","Aria","Scarlett","Penelope","Layla","Chloe",
            "Victoria","Madison","Eleanor","Nora","Riley","Zoey","Hazel","Lily","Ellie","Stella",
            "Paisley","Aurora","Addison","Brooklyn","Lucy","Bella","Claire","Skylar","Savannah","Genesis",
            "Aaliyah","Kennedy","Kinsley","Allison","Maya","Willow","Naomi","Elena","Ariana","Gabriella",
            "Madelyn","Cora","Ruby","Eva","Serenity","Autumn","Adeline","Hailey","Gianna","Valentina",
            "Isla","Eliana","Quinn","Nevaeh","Ivy","Sadie","Piper","Lydia","Alexa","Tessa",
            "Callie","Josie","Molly","Lacey","Bailey","Reagan","Mackenzie","Kylie","Sienna","Hope",
            "Faith","Joy","Rose","Daisy","Pearl","Irene","Wanda","Peggy","Connie","Sheila",
            "Kathy","Cindy","Tina","Debbie","Rhonda","Becky","Jodi","Traci","Kristi","Mindy",
            "Stacy","Brandi","Heidi","Leah","Kara","Adelaide","Alana","Alina","Alison","Amanda",
            "Amara","Anaya","Angelina","Anika","Anita","Annabelle","April","Arabella","Ariel","Aspen",
            "Athena","Aubrey","August","Beatrice","Bianca","Blair","Brianna","Brooke","Brynn","Cadence",
            "Caitlin","Camille","Candace","Carly","Carmen","Cassandra","Cassidy","Cecilia","Celeste","Chelsea",
            "Cheyenne","Christina","Cindy","Clara","Clementine","Colette","Courtney","Crystal","Dahlia","Daisy",
            "Dakota","Daphne","Delaney","Delilah","Destiny","Diana","Dixie","Eden","Edith","Elaine",
            "Elena","Elise","Eliza","Eloise","Ember","Emerson","Emery","Erin","Esme","Esther",
            "Etta","Felicity","Fiona","Freya","Gemma","Georgia","Giselle","Gladys","Gloria","Greta",
            "Gwendolyn","Hadley","Haley","Harmony","Hayley","Heather","Helena","Holly","Imogen","Iris",
            "Isabel","Ivy","Jade","Jasmine","Jenna","Jill","Joanna","Jocelyn","Jordan","Josephine",
            "Juliana","Juliet","June","Kaitlyn","Kara","Kate","Katherine","Katie","Kaylee","Keira",
            "Kelsey","Kendall","Kendra","Kim","Kira","Kristen","Lana","Lara","Laura","Laurel",
            "Lauren","Leah","Leila","Lena","Leslie","Lila","Lilian","Lillian","Lola","London",
            "Lorelei","Louise","Lucia","Lucille","Lydia","Lynn","Mabel","Mackenzie","Madeline","Mae",
            "Maeve","Maggie","Maisie","Mallory","Mara","Maren","Margaret","Maria","Mariah","Marie",
            "Marilyn","Marissa","Martha","Mary","Matilda","Megan","Melanie","Melissa","Melody","Meredith",
            "Mia","Michelle","Mila","Millie","Miranda","Miriam","Molly","Monica","Morgan","Mya",
            "Nadia","Nancy","Naomi","Natalia","Natalie","Nicole","Nina","Noelle","Nora","Nova",
            "Olive","Olivia","Opal","Paige","Paisley","Pamela","Patricia","Paula","Payton","Pearl",
            "Penelope","Penny","Phoebe","Piper","Poppy","Priscilla","Quinn","Rachel","Raegan","Rebecca",
            "Reese","Regina","Renee","Riley","Rita","Rosa","Rosalie","Rose","Rosie","Rowan",
            "Ruby","Ruth","Sabrina","Sadie","Sage","Samantha","Sandra","Sara","Sarah","Sasha",
            "Savannah","Scarlett","Selena","Serena","Sharon","Sheila","Shelby","Shirley","Sienna","Sierra",
            "Skye","Sofia","Sophia","Stella","Stephanie","Summer","Susan","Sydney","Sylvia","Talia",
            "Tamara","Tanya","Tara","Tatum","Taylor","Teagan","Teresa","Tessa","Thea","Theresa",
            "Tiffany","Tina","Tracy","Trinity","Valeria","Valerie","Vanessa","Vera","Veronica","Victoria",
            "Violet","Virginia","Vivian","Wendy","Whitney","Willa","Willow","Winnie","Yasmin","Zoe","Zoey"
        };

        // =====================================================================
        // MIDDLE (~120 each)
        // =====================================================================
        private static readonly string[] MaleMiddle =
        {
            "James","John","Robert","Michael","William","David","Richard","Joseph","Thomas","Charles",
            "Lee","Ray","Dean","Scott","Allen","Wayne","Dale","Gene","Lynn","Alexander",
            "Anthony","Christopher","Daniel","Edward","Eric","Frank","George","Henry","Isaac","Jack",
            "Jacob","Joel","Kenneth","Kyle","Lawrence","Louis","Mark","Martin","Nathan","Nicholas",
            "Owen","Paul","Peter","Samuel","Stephen","Timothy","Victor","Walter","Wesley","Alan",
            "Bruce","Caleb","Elliot","Graham","Harold","Jeffrey","Leon","Miles","Neil","Oscar",
            "Phillip","Riley","Shawn","Travis","Warren","Blake","Cole","Grant","Hayes","Jameson",
            "Kane","Lane","Nash","Reid","Sloan","Trent","Andrew","Benjamin","Carl","Clark",
            "Douglas","Earl","Floyd","Gary","Howard","Ian","Jay","Keith","Lloyd","Max",
            "Norman","Patrick","Quentin","Ralph","Steven","Todd","Vernon","Wade","Xavier","Zachary",
            "Beau","Cruz","Drew","Finn","Gage","Hugh","Jude","Knox","Luke","Leroy","Noah"
        };

        private static readonly string[] FemaleMiddle =
        {
            "Marie","Ann","Lynn","Grace","Rose","Nicole","Elaine","Faith","Jane","Renee",
            "Alice","Amber","Beth","Camille","Dawn","Denise","Diana","Ellen","Faye","Gail",
            "Helen","Hope","Irene","Jean","Joy","June","Kate","Kay","Leigh","Louise",
            "May","Michelle","Naomi","Olivia","Paige","Pearl","Ruth","Sage","Sharon","Sue",
            "Theresa","Valerie","Violet","Wendy","Yvonne","Adele","Brielle","Celeste","Daphne","Estelle",
            "Frances","Harper","Isabel","Joan","Lydia","Mabel","Nadine","Opal","Quinn","Selene",
            "Anne","Claire","Eve","Jade","Lane","Mae","Noel","Rae","Sky","True",
            "Belle","Blair","Brooke","Cate","Drew","Eden","Faye","Gray","Hart","Iris",
            "Jules","Kai","Lux","Moon","Nell","Oak","Piper","Quinn","Rain","Sage",
            "Tess","Uma","Vera","Wren","York","Zoe","Ashley","Christine","Kuuipo","Elizabeth","Margaret"
        };

        // =====================================================================
        // LAST NAMES — EUROPEAN / MIDWEST (~220)
        // =====================================================================
        private static readonly string[] EuropeanLast =
        {
            "Smith","Johnson","Williams","Brown","Jones","Miller","Davis","Wilson","Anderson","Taylor",
            "Thomas","Moore","Jackson","Martin","Lee","Thompson","White","Harris","Clark","Lewis",
            "Robinson","Walker","Young","Allen","King","Wright","Scott","Hill","Green","Adams",
            "Nelson","Baker","Hall","Campbell","Mitchell","Carter","Roberts","Phillips","Evans","Turner",
            "Parker","Edwards","Collins","Stewart","Morris","Murphy","Cook","Rogers","Morgan","Cooper",
            "Peterson","Bailey","Reed","Kelly","Howard","Ward","Richardson","Watson","Brooks","Wood",
            "James","Bennett","Gray","Hughes","Price","Sanders","Myers","Long","Ross","Foster",
            "Powell","Jenkins","Perry","Russell","Sullivan","Bell","Coleman","Butler","Henderson","Crawford",
            "Graham","Wallace","West","Cole","Porter","Hunt","Owen","Fisher","Hart","Gibson",
            "Webb","Tucker","Hayes","Ford","Hamilton","Reynolds","Hicks","Boggs","Conley","McCoy",
            "Hatfield","Yoder","Hostetler","Schrock","Troyer","Weaver","Burkholder","Hershberger","Gingerich","Mast",
            "Helmuth","Raber","Snyder","Shaffer","Keim","Bontrager","Schlabach","Mullet","Stoltzfus","Beiler",
            "Patterson","Barnes","Fisher","Hunter","Palmer","Mills","Rose","Stone","Knight","Burns",
            "Spencer","Gardner","Payne","Pierce","Berry","Matthews","Wagner","Willis","Ray","Watkins",
            "Olson","Carroll","Duncan","Snyder","Hartman","Keller","Hoffman","Schultz","Meyer","Schmidt",
            "Weber","Koch","Bauer","Wolf","Schrader","Kramer","Vogel","Fuchs","Hahn","Berg",
            "Lind","Bergman","Larson","Hansen","Jensen","Pedersen","Nielsen","Christensen","Andersen","Erikson",
            "McDonald","McCarthy","OBrien","Murphy","Kelly","Sullivan","Walsh","Ryan","Burke","Doyle",
            "Fitzgerald","Kennedy","Quinn","Carroll","Gallagher","Lynch","Brennan","Farrell","Connolly","Dunn",
            "Murray","Reid","Cameron","Fraser","Gordon","Grant","Stewart","Campbell","MacDonald","Robertson",
            "Black","White","Gray","Green","Brown","Shaw","Fox","Hawk","Crane","Dove",
            "Abbott","Archer","Barber","Bishop","Carpenter","Cullen","Slayback","Carter","Chandler","Chapman","Clarke","Cooper"
        };

        private static readonly string[] AfricanLast =
        {
            "Washington","Jefferson","Jackson","Harris","Robinson","Thompson","Lewis","Young","Allen","King",
            "Scott","Green","Walker","Wright","Hill","Mitchell","Taylor","Brown","Davis","Clark",
            "Moore","Hall","Anderson","Thomas","White","Brooks","Edwards","Parker","Evans","Collins",
            "Stewart","Morris","Carter","Phillips","Campbell","Banks","Booker","Freeman","Hayes","Jenkins",
            "Jordan","Marshall","Patterson","Perry","Porter","Reed","Sanders","Simmons","Simpson","Stone",
            "Tucker","Wallace","Ward","Watkins","Watson","Webb","Wells","West","Williams","Wilson",
            "Woods","Bryant","Butler","Coleman","Dixon","Franklin","Gibson","Grant","Hawkins","Henderson",
            "Hopkins","Hunter","Johnson","Jones","Lawson","Madison","Mason","Montgomery","Owens","Payne",
            "Peterson","Powell","Price","Ramsey","Reynolds","Richards","Richardson","Ross","Shaw","Spencer",
            "Stephens","Stevens","Turner","Vaughn","Wade","Warren","Waters","Weaver","Wiley","Willis"
        };

        private static readonly string[] LatinoLast =
        {
            "Garcia","Martinez","Rodriguez","Lopez","Hernandez","Gonzalez","Perez","Sanchez","Ramirez","Torres",
            "Flores","Rivera","Gomez","Diaz","Reyes","Cruz","Morales","Ortiz","Gutierrez","Chavez",
            "Ramos","Ruiz","Alvarez","Castillo","Jimenez","Mendoza","Romero","Herrera","Medina","Aguilar",
            "Vargas","Castro","Fernandez","Guzman","Soto","Contreras","Salazar","Delgado","Vega","Guerrero",
            "Rojas","Molina","Navarro","Espinoza","Sandoval","Campos","Cervantes","Dominguez","Leon","Pena",
            "Rios","Soto","Silva","Vargas","Acosta","Aguirre","Alarcon","Arellano","Avila","Bautista",
            "Bernal","Calderon","Cardenas","Carrillo","Castaneda","Cervantes","Cortez","Davila","Delacruz","Escobar",
            "Estrada","Franco","Gallegos","Ibarra","Juarez","Lara","Macias","Maldonado","Mejia","Miranda",
            "Montoya","Nunez","Ochoa","Pacheco","Padilla","Pineda","Quintero","Robles","Rosales","Salas",
            "Salinas","Serrano","Solis","Tapia","Valdez","Valencia","Vasquez","Velasquez","Zamora","Zuniga"
        };

        private static readonly string[] AsianLast =
        {
            "Kim","Lee","Park","Choi","Jung","Kang","Cho","Yoon","Jang","Lim",
            "Chen","Wang","Li","Zhang","Liu","Yang","Huang","Zhao","Wu","Zhou",
            "Nguyen","Tran","Le","Pham","Hoang","Phan","Vu","Dang","Bui","Do",
            "Patel","Singh","Sharma","Kumar","Shah","Gupta","Khan","Ali","Ahmed","Rahman",
            "Tanaka","Sato","Suzuki","Takahashi","Watanabe","Ito","Yamamoto","Nakamura","Kobayashi","Kato",
            "Wong","Chan","Lam","Cheung","Ho","Ng","Lau","Cheng","Tsang","Yeung",
            "Lin","Xu","Sun","Ma","Zhu","Hu","Guo","He","Gao","Luo",
            "Song","Tang","Han","Feng","Dong","Xiao","Cheng","Cao","Deng","Peng",
            "Bai","Cui","Yuan","Pan","Lu","Chang","Hsieh","Chiu","Chung","Hwang",
            "Shin","Yoo","Hong","Oh","Seo","Kwon","Baek","Nam","Moon","Ryu"
        };

        private static readonly string[] MiddleEasternLast =
        {
            "Haddad","Karim","Nassar","Saleh","Farouk","Aziz","Rahman","Khalil","Yasin","Barakat",
            "Saad","Hakim","Hussein","Ismail","Tariq","Basir","Abbas","Amari","Boulos","Darwish",
            "Fadel","Ghanem","Hassan","Ibrahim","Jaber","Khoury","Mansour","Nader","Omar","Qasim",
            "Rashid","Salim","Taha","Youssef","Zaki","Awad","Bitar","Dajani","Farah","Ghazi",
            "Hamid","Issa","Jamil","Kamel","Latif","Mahmoud","Nasser","Osman","Qureshi","Rami",
            "Sami","Tarek","Usman","Wahab","Yasin","Zaman","Alami","Bazzi","Chahine","Dib",
            "Eid","Fawaz","Gibran","Habib","Issawi","Joud","Karam","Lahoud","Mikhail","Nassar"
        };

        private static readonly string[] PacificLast =
        {
            "Kaimana","Kealoha","Manu","Loto","Fale","Tupu","Matai","Alofa","Siva","Peni",
            "Tama","Kele","Malu","Sione","Leka","Nalu","Hana","Makoa","Noa","Keoni",
            "Ikaika","Kahale","Mahina","Leilani","Tui","Vili","Sefo","Anaru","Wiremu","Hemi",
            "Aroha","Mana","Tane","Wai","Kai","Lani","Moana","Nui","Pua","Rangi",
            "Talia","Vai","Aka","Benji","Caleb","Davu","Enele","Fili","Gafa","Hemi"
        };

        private static readonly string[] MixedLast =
        {
            "Reed","Gray","Brooks","Cole","Diaz","Nguyen","Patel","Santos","Rivera","King",
            "Bennett","Hayes","Price","Foster","Grant","Stone","West","Lane","Ford","Hart",
            "Cross","Shaw","Wells","Page","Blair","Quinn","Drew","Casey","Riley","Morgan",
            "Jordan","Taylor","Cameron","Hayden","Parker","Harper","Avery","Reese","Sage","Rowan",
            "Ellis","Finley","Hayden","Jordan","Kai","Logan","Morgan","Peyton","Quinn","Riley",
            "Sawyer","Taylor","Cameron","Dakota","Emerson","Finley","Harper","Jamie","Kennedy","London",
            "Marley","Parker","Reese","River","Skyler","Tatum","Winter","Ash","Blake","Charlie"
        };

        // =====================================================================
        // NICKNAMES
        // =====================================================================
        public static string GenerateNickname(string gender, NpcTraits? traits = null)
        {
            if (traits != null)
            {
                if (traits.Get("trait.pride") >= 70 || traits.Get("trait.hope") >= 72) return Pick(NickBrave);
                if (traits.Get("trait.anger") >= 70 || traits.Get("trait.resentment") >= 70) return Pick(NickAggressive);
                if (traits.Get("trait.affection") >= 72 || traits.Get("trait.openness") >= 70) return Pick(NickKind);
                if (traits.Get("trait.guard") >= 70 || traits.Get("trait.anxiety") >= 70) return Pick(NickShy);
                if (traits.Get("trait.playfulness") >= 70) return Pick(NickFunny);
                if (traits.Get("trait.tension") >= 72 || traits.Get("trait.desire") >= 75) return Pick(NickTrouble);
                if (traits.Get("trait.patience") >= 72) return Pick(NickCalm);
                if (traits.Get("trait.loneliness") >= 70 || traits.Get("trait.hurt") >= 70) return Pick(NickSoft);
            }
            return IsMale(gender) ? Pick(NickMaleFallback) : Pick(NickFemaleFallback);
        }

        private static readonly string[] NickBrave =
        {
            "Ace","Chief","Ranger","Hawk","Maverick","Blaze","Iron","Titan","Valor","Grit",
            "Scout","Captain","Duke","Knight","Storm","Ace","Bolt","Flash","Hero","Legend",
            "Prime","Ridge","Summit","Torch","Vanguard","Warden","Arrow","Blade","Crown","Eagle"
        };
        private static readonly string[] NickAggressive =
        {
            "Bull","Gator","Spike","Rex","Crusher","Knuckles","Bruiser","Fang","Viper","Tank",
            "Hammer","Wolf","Bear","Razor","Brick","Bone","Clamp","Diesel","Fist","Grind",
            "Havoc","Jolt","Mace","Nail","Outlaw","Punch","Riot","Savage","Thorn","War"
        };
        private static readonly string[] NickKind =
        {
            "Sunny","Honey","Angel","Peaches","Dove","Blossom","Smiley","Breeze","Hope","Heart",
            "Sugar","Kit","Gem","Daisy","Joy","Buddy","Cherub","Clover","Darling","Ember",
            "Flower","Glow","Harmony","Ivory","Jewel","Kindred","Light","Maple","Nest","Opal"
        };
        private static readonly string[] NickShy =
        {
            "Whisper","Mouse","Shadow","Quiet","Softie","Flicker","Shade","Moth","Pebble","Tiny",
            "Hush","Mist","Gray","Lowkey","Ghost","Blur","Dust","Echo","Fog","Haze",
            "Ink","Lurk","Mute","Nook","Owl","Pale","Shy","Silk","Veil","Wisp"
        };
        private static readonly string[] NickFunny =
        {
            "Goofy","Joker","Bubbles","Giggles","Snickers","Wiggles","Zippy","Scooter","Pickles","Noodles",
            "Waffles","Chip","Buzz","Sparky","Banjo","Beans","Bingo","Bozo","Chuckles","Doodle",
            "Fizz","Gumbo","Hiccup","Jelly","Kook","Loopy","Muffin","Nugget","Pogo","Quirk"
        };
        private static readonly string[] NickTrouble =
        {
            "Slick","Rowdy","Rascal","Bandit","Wildcard","Chaos","Spook","Trickster","Jinx","Rebel",
            "Rogue","Fox","Dice","Risk","Vex","Ace","Blitz","Coyote","Drift","Edge",
            "Fury","Gambit","Hustle","Joker","Knave","Lurk","Mischief","Phantom","Rogue","Shade"
        };
        private static readonly string[] NickCalm =
        {
            "Chill","Zen","Still","Cloud","Drift","River","Mellow","Peace","Sage","Calm",
            "Blue","Stone","Oak","Tide","Quiet","Anchor","Balm","Cove","Dawn","Ease",
            "Fern","Grove","Harbor","Isle","Lake","Moss","Pine","Rest","Shore","Vale"
        };
        private static readonly string[] NickSoft =
        {
            "Ember","Soft","Moon","Rain","Willow","Ash","Hollow","Pale","Fawn","Lark",
            "Nettle","Moss","Iris","Snow","Gray","Bloom","Drift","Feather","Haze","Linen",
            "Mist","Petal","Silk","Thistle","Violet","Wisp","Yarrow","Cloud","Dew","Frost"
        };
        private static readonly string[] NickMaleFallback =
        {
            "Buddy","Duke","Rocky","Moose","Bear","Lucky","Rusty","Boomer","Rowdy","Wally",
            "Bubba","Red","Junior","Skip","Tex","Ace","Buster","Chip","Corky","Dutch",
            "Hoss","Mac","Oz","Sarge","Slim","Spike","Tater","Turk","Woody","Zip"
        };
        private static readonly string[] NickFemaleFallback =
        {
            "Lulu","Kitty","Roxy","Dolly","Sunny","Star","Cherry","Goldie","Pixie","Bambi",
            "Sassy","Coco","Missy","Gigi","Trixie","Babe","Candy","Dixie","Foxy","Ginger",
            "Honey","Jinx","Kiki","Lola","Mimi","Nina","Peach","Queenie","Rosie","Suki"
        };

        // Dirty / dark / reaction — same as previous message (keep those methods)
        // Paste GenerateDirtyName, GenerateDarkName, GetNameReactionScore, ApplyNameReaction
        // from the last full file if you already have them; unchanged logic.

        public static string GenerateDirtyName(string gender, NpcTraits? traits = null)
        {
            if (traits != null)
            {
                float desire = traits.Get("trait.desire");
                float shame = traits.Get("trait.shame");
                float openness = traits.Get("trait.openness");
                float guard = traits.Get("trait.guard");
                if (desire >= 75 && shame < 40 && openness >= 55) return Pick(DirtyHarsh);
                if (traits.Get("trait.affection") >= 65 && desire >= 55) return Pick(DirtySoft);
                if (guard >= 70) return Pick(DirtySoft);
            }
            if (IsMale(gender)) return Pick(DirtyMale);
            if (IsFemale(gender)) return Pick(DirtyFemale);
            return Pick(DirtyShared);
        }

        private static readonly string[] DirtyFemale =
        {
            "slut","whore","good girl","dirty girl","needy slut","fucktoy","cumslut","pet","kitten","doll",
            "toy","princess","baby girl","little slut","filthy girl","open slut","hungry slut","wet slut","used slut","owned slut",
            "mouth slut","throat slut","secret slut","pretty slut","needy whore","loyal whore","personal whore","fuckdoll","cumdump","seed slut",
            "breeding slut","freeuse slut","public slut","quiet slut","moaning slut","begging slut","ruined slut","marked slut","claimed slut","daddy's girl",
            "good little slut","filthy little thing","needy little hole","pretty little toy","sweet slut","eager slut","shameless slut","cock-drunk slut","wet little whore","loyal little pet",
            "dirty little secret","favorite slut","personal toy","bed slut","night slut","soft slut","easy slut","cheap slut","house whore","corner whore",
            "eager mouth","kneeling slut","leashed pet","open toy","soft hole","trembling slut","willing doll","yours","kept girl","shared slut"
        };
        private static readonly string[] DirtyMale =
        {
            "good boy","dirty boy","needy boy","cock slut","fucktoy","pet","doll","toy","slut","whore",
            "eager slut","mouth slut","throat slut","owned slut","used slut","pathetic slut","loyal slut","personal slut","bedroom slut","secret slut",
            "filthy boy","desperate slut","ruined slut","broken slut","marked slut","claimed slut","fuckdoll","cumdump","freeuse slut","public slut",
            "quiet slut","moaning slut","begging slut","daddy's boy","good little slut","filthy little thing","pretty little toy","sweet slut","eager slut","shameless slut",
            "loyal little pet","dirty little secret","favorite slut","personal toy","needy hole","cock-hungry","used-up slut","open slut","night slut","bed slut",
            "kneeling boy","leashed pet","open toy","willing doll","yours","kept boy","shared slut","eager mouth","soft hole","trembling slut"
        };
        private static readonly string[] DirtyShared =
        {
            "slut","whore","fucktoy","cumslut","pet","toy","doll","needy slut","owned slut","used slut",
            "good little slut","filthy thing","personal whore","secret slut","freeuse slut","begging slut","ruined slut","claimed slut","marked slut","cock-drunk",
            "open","kept","shared","leashed","kneeling","willing","eager","soft","yours","mine"
        };
        private static readonly string[] DirtyHarsh =
        {
            "slut","whore","cumslut","fucktoy","cumdump","used slut","pathetic slut","freeuse slut","ruined slut","seed slut",
            "cock whore","dirty little hole","open hole","broken slut","cheap whore","public slut","cumrag","fuckhole","owned thing","used-up",
            "trash","meat","hole","dump","rag","nothing","spare","object","property","thing"
        };
        private static readonly string[] DirtySoft =
        {
            "good girl","good boy","pet","kitten","princess","baby","doll","good little slut","loyal pet","sweet slut",
            "pretty toy","favorite","angel","honey","baby girl","baby boy","soft thing","pretty thing","mine","darling",
            "sweetness","treasure","peach","dove","lamb","dear","love","sugar","star","jewel"
        };

        public static string GenerateDarkName(string gender, NpcTraits? traits = null)
        {
            if (traits != null)
            {
                if (traits.Get("trait.guard") >= 75 || traits.Get("trait.resentment") >= 70) return Pick(DarkCold);
                if (traits.Get("trait.desire") >= 75 && traits.Get("trait.tension") >= 60) return Pick(DarkIntimate);
            }
            return Pick(DarkNames);
        }

        private static readonly string[] DarkNames =
        {
            "Ghost","Venom","Razor","Hollow","Shade","Sable","Ash","Noir","Vex","Ruin",
            "Crow","Thorn","Wraith","Spite","Cold","Null","Hush","Bleak","Widow","Viper",
            "Sorrow","Grave","Static","Echo","Hex","Omen","Raven","Dusk","Frost","Steel",
            "Bane","Cinder","Marrow","Scar","Nails","Hook","Quiet","Still","Blank","Sever",
            "Knot","Chain","Lock","Brand","Mark","Claim","Own","Keep","Cage","Doll",
            "Abyss","Bleak","Cipher","Dirge","Eclipse","Fang","Gloom","Harbinger","Ice","Jinx"
        };
        private static readonly string[] DarkIntimate =
        {
            "mine","property","thing","object","pet","doll","owned","claimed","kept","used",
            "ruined","marked","open","soft","quiet","still","bound","leashed","taken","held",
            "yours","kept","shared","claimed","marked","owned","bound","open","soft","mine"
        };
        private static readonly string[] DarkCold =
        {
            "thing","object","hole","used","ruined","broken","property","doll","blank","null",
            "spare","spare part","nobody","empty","cold","gone","cut","done","trash","ash",
            "void","waste","leftover","discard","husk","shell","mask","ghost","cinder","dust"
        };

        public static int GetNameReactionScore(string usedName, NpcTraits traits)
        {
            if (traits == null || string.IsNullOrWhiteSpace(usedName)) return 0;
            string name = usedName.Trim().ToLowerInvariant();
            int score = 0;
            bool isDirty = name.Contains("slut") || name.Contains("whore") || name.Contains("fucktoy") ||
                name.Contains("cum") || name.Contains("toy") || name.Contains("pet") ||
                name.Contains("doll") || name.Contains("hole") || name.Contains("dump") ||
                name.Contains("freeuse") || name.Contains("seed") || name.Contains("used");
            bool isSoftDirty = name.Contains("good girl") || name.Contains("good boy") || name.Contains("princess") ||
                name.Contains("baby") || name.Contains("kitten") || name.Contains("angel") ||
                name.Contains("honey") || name.Contains("darling");
            bool isDark = name.Contains("mine") || name.Contains("property") || name.Contains("object") ||
                name.Contains("owned") || name.Contains("claimed") || name.Contains("ruined") ||
                name.Contains("broken") || name.Contains("thing");
            float desire = traits.Get("trait.desire");
            float affection = traits.Get("trait.affection");
            float openness = traits.Get("trait.openness");
            float shame = traits.Get("trait.shame");
            float guard = traits.Get("trait.guard");
            float trust = traits.Get("trait.trust");
            if (isDirty)
            {
                if (desire >= 60) score += 4;
                if (openness >= 60) score += 2;
                if (shame >= 60) score -= 4;
                if (desire <= 30) score -= 3;
                if (guard >= 70) score -= 2;
            }
            if (isSoftDirty)
            {
                if (affection >= 60) score += 4;
                if (openness >= 50) score += 2;
                if (desire >= 80 && shame < 30) score -= 1;
            }
            if (isDark)
            {
                if (desire >= 60) score += 3;
                if (trust >= 60 && openness >= 55) score += 2;
                if (guard >= 70 || shame >= 60) score -= 3;
            }
            if (desire >= 70 && score > 0) score += 1;
            return Math.Clamp(score, -10, 10);
        }

        public static void ApplyNameReaction(NpcTraits traits, string usedName)
        {
            if (traits == null || string.IsNullOrWhiteSpace(usedName)) return;
            int score = GetNameReactionScore(usedName, traits);
            if (score >= 3)
            {
                traits.Adjust("trait.desire", +2);
                traits.Adjust("trait.attraction", +1);
                traits.Adjust("trait.shame", -1);
            }
            else if (score <= -3)
            {
                traits.Adjust("trait.desire", -2);
                traits.Adjust("trait.shame", +2);
                traits.Adjust("trait.anxiety", +1);
                traits.Adjust("trait.guard", +1);
            }
            else if (score > 0) traits.Adjust("trait.desire", +1);
            else if (score < 0)
            {
                traits.Adjust("trait.shame", +1);
                traits.Adjust("trait.guard", +1);
            }
        }

        private static string Pick(string[] pool) => pool[Rng.Next(pool.Length)];
        private static bool IsMale(string gender)
            => string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase)
            || string.Equals(gender, "M", StringComparison.OrdinalIgnoreCase);
        private static bool IsFemale(string gender)
            => string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase)
            || string.Equals(gender, "F", StringComparison.OrdinalIgnoreCase);
    }
}