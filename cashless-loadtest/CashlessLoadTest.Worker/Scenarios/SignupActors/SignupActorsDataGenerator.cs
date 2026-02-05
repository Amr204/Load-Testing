namespace CashlessLoadTest.Worker.Scenarios.SignupActors;

/// <summary>
/// Generated actor data for registration.
/// </summary>
public class GeneratedActorData
{
    public string Mobile { get; set; } = "";
    public string UserName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string SecondName { get; set; } = "";
    public string ThirdName { get; set; } = "";
    public string LastName { get; set; } = "";
}

/// <summary>
/// Generates unique Mobile/UserName for load testing.
/// Format: 7{TelcoDigit}{NodeId:00}{WorkloadDigit}{Seq:0000} = 9 digits
/// </summary>
public class SignupActorsDataGenerator
{
    private static long _globalCounter = 0;
    private readonly SignupActorsSettings _settings;
    private static readonly ThreadLocal<Random> _random = new(() => new Random(Guid.NewGuid().GetHashCode()));

    // Large name lists (100+ names)
    private static readonly string[] FirstNames = new[]
    {
        "Ahmed", "Mohamed", "Ali", "Omar", "Youssef", "Hassan", "Hussein", "Khaled", "Ibrahim", "Mahmoud",
        "Said", "Nasser", "Faisal", "Tariq", "Walid", "Karim", "Rami", "Sami", "Adel", "Jamal",
        "Rashid", "Hamad", "Sultan", "Fahad", "Saleh", "Majid", "Abdullah", "Abdulrahman", "Abdulaziz", "Mansour",
        "Nabil", "Hani", "Amr", "Mustafa", "Ziad", "Bilal", "Osama", "Tarek", "Hazem", "Mohannad",
        "Anas", "Younes", "Ayman", "Hisham", "Wael", "Bassam", "Marwan", "Saad", "Khalil", "Imad",
        "Jamil", "Munir", "Samir", "Nizar", "Tamer", "Shadi", "Fadi", "Riad", "Maher", "Akram",
        "Ashraf", "Ehab", "Essam", "Gamal", "Hatem", "Ihab", "Ismail", "Lotfi", "Magdi", "Mohsen",
        "Nabeel", "Rafik", "Ramadan", "Reda", "Sabri", "Sherif", "Sobhi", "Wahid", "Yasser", "Zakaria",
        "Abdo", "Amer", "Badr", "Chakib", "Djamel", "Elias", "Fouad", "Ghassan", "Habib", "Jalal",
        "Kais", "Laith", "Malik", "Naim", "Othman", "Qasim", "Raed", "Safwan", "Talal", "Usama"
    };

    private static readonly string[] MiddleNames = new[]
    {
        "Ahmad", "Mohammed", "Salim", "Yusuf", "Hamid", "Rashid", "Jabir", "Sadiq", "Shakir", "Nasir",
        "Latif", "Rauf", "Ghani", "Hafiz", "Kadir", "Majid", "Rahim", "Salam", "Wahab", "Basir",
        "Fatah", "Hakim", "Kabir", "Malik", "Nafi", "Qadir", "Rafiq", "Samad", "Wadud", "Zahir",
        "Alim", "Aziz", "Bari", "Ghafir", "Halim", "Jabbar", "Khaliq", "Matin", "Nasir", "Qayyum",
        "Razzaq", "Sami", "Tawwab", "Wakil", "Zaki", "Mumin", "Muhyi", "Mujib", "Mumit", "Muqit",
        "Fattah", "Falah", "Faris", "Fawzi", "Fikri", "Fuad", "Ghalib", "Hadi", "Hamdi", "Hanif",
        "Hashim", "Hikmat", "Hilmi", "Humam", "Hussam", "Idris", "Ihsan", "Jibril", "Kamil", "Labib",
        "Mahdi", "Mubarak", "Mumtaz", "Mundhir", "Musa", "Nadir", "Najib", "Nazim", "Numan", "Nuri",
        "Qahtan", "Rabi", "Rashed", "Rawhi", "Saber", "Sadiq", "Safar", "Sahir", "Sajid", "Salah",
        "Samih", "Shafiq", "Sharif", "Suhaib", "Sulaiman", "Tahir", "Tawfiq", "Ubaid", "Wafi", "Yamin"
    };

    private static readonly string[] LastNames = new[]
    {
        "Alzahrani", "Alqahtani", "Alotaibi", "Alharbi", "Alshamrani", "Aldosari", "Almutairi", "Alghamdi", "Albalawi", "Alomari",
        "Alharthi", "Alshehri", "Almohammadi", "Alyami", "Alsubaie", "Abadi", "Adawi", "Ahmadi", "Akbari", "Alavi",
        "Amini", "Ansari", "Asadi", "Azimi", "Bagheri", "Bahrami", "Darvish", "Ebrahimi", "Eskandari", "Farahani",
        "Faraji", "Ghaderi", "Ghanbari", "Gholami", "Habibi", "Hashemi", "Hosseini", "Jafari", "Jalali", "Jamali",
        "Kamali", "Karimi", "Kazemi", "Khani", "Khodadadi", "Lotfi", "Mahmoudi", "Maleki", "Mansouri", "Mohammadi",
        "Moradi", "Mousavi", "Najafi", "Naseri", "Nazari", "Nikbakht", "Noori", "Omidi", "Pakdaman", "Parsa",
        "Qasemi", "Rahmani", "Rahimi", "Rashidi", "Rezaei", "Sadeghi", "Saeedi", "Safavi", "Salimi", "Samani",
        "Seifi", "Shahriari", "Sharifi", "Shirazi", "Soltani", "Taheri", "Tahmasebi", "Tavakoli", "Yousefi", "Zamani",
        "Abbasi", "Afzali", "Akhavan", "Alamdar", "Alipour", "Amiri", "Arbabi", "Ataei", "Azizi", "Badri",
        "Bakhtiari", "Basiri", "Bayat", "Beheshti", "Dabiri", "Danesh", "Davari", "Dehghani", "Emami", "Fadavi"
    };

    public SignupActorsDataGenerator(SignupActorsSettings settings)
    {
        _settings = settings;
        Console.WriteLine($"[DataGenerator] NodeId: {_settings.WorkerNodeId:D2}, TelcoDigits: [{string.Join(",", _settings.TelcoDigits)}]");

#if DEBUG
        // Self-check: generate 5000 mobiles and verify no duplicates within process
        if (Environment.GetEnvironmentVariable("SELFCHECK_DUPLICATES") == "1")
        {
            SelfCheckDuplicates(5000);
        }
#endif
    }

    /// <summary>
    /// Generates unique actor data for registration.
    /// Mobile format: 7{TelcoDigit}{NodeId:00}{WorkloadDigit}{Seq:0000} = 9 digits
    /// where TelcoDigit ∈ {0,1,3,7,8}, NodeId = 00-99, W = workloadIndex % 10, Seq = 0000-9999
    /// </summary>
    public GeneratedActorData Generate(int workloadIndex)
    {
        var counter = Interlocked.Increment(ref _globalCounter);
        var random = _random.Value!;

        // Mobile: 7{T}{NN}{W}{SSSS} = 9 digits
        var telcoDigits = _settings.TelcoDigits;
        var T = telcoDigits[random.Next(telcoDigits.Length)]; // Yemen telco: 70,71,73,77,78
        var NN = _settings.WorkerNodeId;                       // 00-99 (worker uniqueness)
        var W = workloadIndex % 10;                            // 0-9 (VU uniqueness)
        var S = counter % 10_000;                              // 0000-9999 (sequence)
        var mobile = $"7{T}{NN:D2}{W}{S:D4}";

        // UserName: same as mobile
        var userName = mobile;

        // Random names from lists
        var firstName = FirstNames[random.Next(FirstNames.Length)];
        var secondName = MiddleNames[random.Next(MiddleNames.Length)];
        var thirdName = MiddleNames[random.Next(MiddleNames.Length)];
        var lastName = LastNames[random.Next(LastNames.Length)];

        return new GeneratedActorData
        {
            Mobile = mobile,
            UserName = userName,
            FirstName = firstName,
            SecondName = secondName,
            ThirdName = thirdName,
            LastName = lastName
        };
    }


    /// <summary>
    /// Converts number to base26 letters (a-z).
    /// </summary>
    private static string ConvertToLetters(long number)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz";
        if (number < 0) number = Math.Abs(number);
        if (number == 0) return "a";

        var result = new char[12];
        var index = result.Length;

        while (number > 0 && index > 0)
        {
            index--;
            result[index] = alphabet[(int)(number % 26)];
            number /= 26;
        }

        // Ensure minimum 2 chars
        while (result.Length - index < 2 && index > 0)
        {
            index--;
            result[index] = 'a';
        }

        return new string(result, index, result.Length - index);
    }

    /// <summary>
    /// Generates random letters.
    /// </summary>
    private static string GenerateRandomLetters(Random random, int length)
    {
        const string letters = "abcdefghijklmnopqrstuvwxyz";
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = letters[random.Next(letters.Length)];
        }
        return new string(result);
    }

    /// <summary>
    /// Gets current counter value.
    /// </summary>
    public static long GetCounter() => Interlocked.Read(ref _globalCounter);

#if DEBUG
    /// <summary>
    /// Self-check: generate N mobiles and assert no duplicates.
    /// </summary>
    private void SelfCheckDuplicates(int count)
    {
        var mobiles = new HashSet<string>();
        Console.WriteLine($"[DataGenerator] Running self-check with {count} mobiles...");

        for (int i = 0; i < count; i++)
        {
            var data = Generate(i % 10); // simulate different workload indices
            if (!mobiles.Add(data.Mobile))
            {
                Console.WriteLine($"[DataGenerator] DUPLICATE FOUND: {data.Mobile}");
            }
        }

        // Log example breakdown
        var example = Generate(5);
        var T = example.Mobile[1];
        var NN = example.Mobile.Substring(2, 2);
        var W = example.Mobile[4];
        var S = example.Mobile.Substring(5, 4);
        Console.WriteLine($"[DataGenerator] Example: {example.Mobile} (T={T}, NN={NN}, W={W}, S={S})");
        Console.WriteLine($"[DataGenerator] Self-check complete. {mobiles.Count}/{count} unique mobiles.");
    }
#endif
}
