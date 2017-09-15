using System;

/// <summary>
/// Adds a Random field to JArray which returns a random entry from the array.
/// Also provides means to seed the randomness.
/// </summary>
public partial class JArray {
  private static Random Random = new Random();

  public JToken Random {
    get {
      if (Count == 0) {
        return null;
      }
      return GetItem(Random.Next(Count));
    }
  }

  public static void SeedRandom(int randomSeed) {
    Random = new Random(randomSeed);
  }
}
