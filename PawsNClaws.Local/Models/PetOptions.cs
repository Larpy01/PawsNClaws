namespace PawsNClaws.Local.Models;

public static class PetOptions
{
    public static IReadOnlyList<string> Sizes { get; } =
        ["Extra small", "Small", "Medium", "Large", "Extra large", "Giant"];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BreedsBySpecies { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cat"] = ["Bengal", "British Shorthair", "Domestic Longhair", "Domestic Shorthair", "Maine Coon", "Mixed breed", "Persian", "Puspin", "Ragdoll", "Russian Blue", "Scottish Fold", "Siamese", "Sphynx"],
            ["Dog"] = ["Aspin", "Beagle", "Belgian Malinois", "Chihuahua", "Chow Chow", "Dachshund", "French Bulldog", "German Shepherd", "Golden Retriever", "Labrador Retriever", "Maltese", "Mixed breed", "Pomeranian", "Poodle", "Pug", "Shih Tzu", "Siberian Husky", "Welsh Corgi", "Yorkshire Terrier"]
        };
}
