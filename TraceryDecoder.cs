using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;
using System.Linq;
using LitJson;
using TraceryNet;

public class TraceryGrammar {

  public string source;
  Dictionary<string, string[]> grammar;

  /// <summary>
  /// Regex that matches a replacement symbol within a string, including any
  /// potential save symbols and modifiers.
  ///
  /// E.g. Input string "If #[pronoun:#pronoun#][verb:#verb#]pronounplusverb.ed.capitalizeAll# to have a nap..."
  /// The regex would break down as follows:
  /// Whole Match: `#[pronoun:#pronoun#][verb:#verb#]pronounplusverb.ed.capitalizeAll#`
  /// - Group `saves`:
  /// - - Value: `verb:#verb`
  /// - - Captures: [`pronoun:#pronoun#`, `verb:#verb#`]
  /// - Group `symbol`:
  /// - - Value: `pronounplusverb`
  /// - Group `modifiers`:
  /// - - Value: `ed`
  /// - - Captures: [`ed`, `capitalizeAll`]
  /// </summary>
  private static readonly Regex ExpansionRegex = new Regex(@"#(?:\[(?<saves>[^\[\]]+)\])*(?<symbol>.*?)(?:\.(?<modifiers>[^\[\]\.#]+))*#");

  /// <summary>
  /// Modifier function table.
  /// </summary>
  public Dictionary<string, Func<string, string>> ModifierLookup;

  /// <summary>
  /// Key/value store for savable data.
  /// </summary>
  public Dictionary<string, List<string>> SaveData;

  private static List<List<string>> listPool = new List<List<string>>();

  [ThreadStatic] private static Random random;

  /// <summary>
  /// RNG to pick from multiple rules.
  /// </summary>
  [ThreadStatic] private static Random Random {
    get {
      return random ?? (random = new Random(unchecked(Environment.TickCount * 31 +
                                            System.Threading.Thread.CurrentThread.ManagedThreadId)));
    }
    set {
      random = value;
    }
  }

  public TraceryGrammar(string source) {
    this.source = source;
    Decode(source);

    // Set up the standard modifiers.
    ModifierLookup = new Dictionary<string, Func<string, string>> {
      {"a", Modifiers.A},
      {"beeSpeak", Modifiers.BeeSpeak},
      {"capitalize", Modifiers.Capitalize},
      {"capitalizeAll", Modifiers.CapitalizeAll},
      {"comma", Modifiers.Comma},
      {"inQuotes", Modifiers.InQuotes},
      {"s", Modifiers.S},
      {"ed", Modifiers.Ed},
      {"titleCase", Modifiers.TitleCase}
    };

    // Initialize SaveData map used for actions.
    SaveData = new Dictionary<string, string>();
  }

  /// <summary>
  /// Resolves the default starting symbol "#origin#".
  /// </summary>
  /// <param name="randomSeed">Reliably seeds "random" selection to provide consistent results.</param>
  /// <returns>A result interpreted from the symbol "#origin#".</returns>
  public string Generate(int? randomSeed = null) {
    return GenerateFromNode("origin", randomSeed);
  }

  /// <summary>
  /// Resolves the provided symbol from the existing grammar rules.
  /// </summary>
  /// <param name="token">Symbol to resolve. Do not provide surrounding #'s.</param>
  /// <param name="randomSeed">Reliably seeds "random" selection to provide consistent results.</param>
  /// <returns>The interpreted output string from the grammar rules.</returns>
  public string GenerateFromNode(string token, int? randomSeed = null) {
    return Resolve(string.Format("#{0}#", token), randomSeed);
  }

  /// <summary>
  /// Resolves all the replacements in a given string with the corresponding saved data or grammar rules.
  /// </summary>
  /// <param name="token">String in which to resolve symbols.</param>
  /// <param name="randomSeed">Reliably seeds "random" selection to provide consistent results.</param>
  /// <returns>The interpreted string according to saved and grammar data.</returns>
  public string Resolve(string token, int? randomSeed = null) {
    if (randomSeed.HasValue) {
      Random = new System.Random(randomSeed.Value);
    }

    // Find expansion matches.
    foreach (Match match in ExpansionRegex.Matches(token)) {
      // Resolve the symbol from saved data or grammar rules, if any.
      var result = ResolveSymbol(match.Groups["symbol"].Value);

      // Store save symbols in SaveData for this symbol only.
      var saveKeys = ResolveSaveSymbols(match.Groups["saves"].Captures);

      // Recursively resolve any nested symbols present in the resolved data.
      result = Resolve(result, randomSeed);

      // Apply modifiers, if any, to the result.
      result = ApplyModifiers(result, match.Groups["modifiers"].Captures);

      // Replace only the first occurance of the match within the input string.
      var first = token.IndexOf(match.Value);
      token = token.Substring(0, first) + result + token.Substring(first + match.Value.Length);

      // Keep saved data in scope of their symbols by popping added keys before continuing.
      PopSaveKeys(saveKeys);
    }

    return token;
  }


  public static List<T> Shuffle<T>(IEnumerable<T> list)
  {
      var output = new List<T>(list);
      int n = output.Count;
      while (n > 1)
      {
          n--;
          int k = Random.Next(n + 1);
          T value = output[k];
          output[k] = output[n];
          output[n] = value;
      }
      return output;
  }


  /// <summary>
  /// Resolve the symbol from save data first, then a grammar rule if nothing saved,
  /// or finally just return the string itself if no saved or grammar substitution.
  /// </summary>
  /// <param name="symbol">The string to match against.</param>
  /// <returns>The interpreted string from save data or grammar, if any.</returns>
  private string ResolveSymbol(string symbol) {
    if (SaveData.ContainsKey(symbol)) {
      return SaveData[symbol].Last();
    }
    if (grammar.ContainsKey(symbol)) {
      return grammar[symbol][Random.Next(grammar[symbol].Length)];
    }
    return symbol;
  }


  /// <summary>
  /// Resolves and saves any data marked by `saves` captures.
  /// </summary>
  /// <param name="saves">Collection of captures from `saves` Group.</param>
  /// <returns>List of pushed save keys. Pop these after symbol resolution.</returns>
  private List<string> ResolveSaveSymbols(CaptureCollection saves) {
    if (saves.Count == 0) {
      return null;
    }
    var pushedDataKeys = GetOrCreateList();

    foreach (Capture capture in saves) {
      var save = capture.Value;

      // If it's just [key], then flatten #key#
      if (!save.Contains(":")) {
        Resolve(string.Format("#{0}#", save));
        continue;
      }

      // For [key:#symbol#], split into key and data.
      var saveSplit = save.Split(':');
      var key = saveSplit[0];
      var data = Resolve(saveSplit[1]);

      // Save resolution of symbol to key in SaveData.
      // If key already exists, push the new value to the end. Otherwise add it.
      pushedDataKeys.Add(key);
      if (SaveData.ContainsKey(key)) {
        SaveData[key].Add(data);
      } else {
        var list = GetOrCreateList();
        list.Add(data);
        SaveData[key] = list;
      }
    }

    return pushedDataKeys;
  }

  private void PopSaveKeys(List<string> keys) {
    if (keys == null || keys.Count == 0) {
      return;
    }
    foreach (var key in keys) {
      if (!SaveData.ContainsKey(key)) {
        continue;
      }
      SaveData[key].RemoveAt(SaveData.Count - 1);
      if (SaveData[key].Count == 0) {
        FreeList(SaveData[key]);
        SaveData.Remove(key);
      }
    }
    FreeList(keys);
  }

  /// <summary>
  /// Helper function to keep from creating lists when unnecessary.
  /// </summary>
  /// <returns>A string list from listPool or a new one.</returns>
  private static List<string> GetOrCreateList() {
    if (listPool.Count == 0) {
      return new List<string>();
    }
    var list = listPool[0];
    listPool.RemoveAt(0);
    return list;
  }

  /// <summary>
  /// Helper function to free a list once it's no longer needed.
  /// </summary>
  /// <param name="list">The list to free.</param>
  private static void FreeList(List<string> list) {
    if (list == null || listPool.Contains(list)) {
      return;
    }
    list.Clear();
    listPool.Add(list);
  }


  private void Decode(string source) {
    grammar = new Dictionary<string, string[]>();
    var map = JsonToMapper(source);
    foreach (var key in map.Keys) {
      if (map[key].IsArray) {
        string[] entries = new string[map[key].Count];
        for (int i = 0; i < map[key].Count; i++) {
          var entry = map[key][i];
          entries[i] = (string) entry;
        }
        grammar.Add(key, entries);
      } else if (map[key].IsString) {
        string[] entries = {map[key].ToString()};
        grammar.Add(key, entries);
      }
    }
  }

  public static JsonData JsonToMapper(string tracery) {
    var traceryStructure = JsonMapper.ToObject(tracery);
    return traceryStructure;
  }

  /// <summary>
  /// Add a modifier to the modifier lookup.
  /// </summary>
  /// <param name="name">The name to identify the modifier with.</param>
  /// <param name="func">A method that returns a string and takes a string as a param.</param>
  public void AddModifier(string name, Func<string, string> func) {
    ModifierLookup[name] = func;
  }

  /// <summary>
  /// Resolve each defined modifier on the expansion data.
  /// </summary>
  /// <param name="data">The string to modify.</param>
  /// <param name="modifiers">Captured modifier names.</param>
  /// <returns>The resolved data with modifiers applied.</returns>
  private string ApplyModifiers(string data, CaptureCollection modifiers) {
    return modifiers.Where(modName => ModifierLookup.ContainsKey(modName))
                    .Select(modName => ModifierLookup[modName])
                    .Aggregate(data, (cume, next) => ModifierLookup[next](cume));
  }
}
