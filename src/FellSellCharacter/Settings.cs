using MelonLoader;

namespace FellSellCharacter;

internal static class Settings
{
    public static MelonPreferences_Entry<bool> Enabled;
    public static MelonPreferences_Entry<string> ViewKey;
    public static MelonPreferences_Entry<string> EditorKey;
    public static MelonPreferences_Entry<bool> ThirdPerson;
    public static MelonPreferences_Entry<float> ShoulderRight;
    public static MelonPreferences_Entry<float> ShoulderUp;
    public static MelonPreferences_Entry<float> Distance;
    public static MelonPreferences_Entry<bool> Wardrobe;
    public static MelonPreferences_Entry<string> WardrobePosition;
    public static MelonPreferences_Entry<bool> DevCommands;

    public static void Bind()
    {
        var c = MelonPreferences.CreateCategory("FellSellCharacter", "Fell & Sell Character");
        Enabled = c.CreateEntry("Enabled", true, description: "Third-person view and the character editor.");
        ViewKey = c.CreateEntry("ViewKey", "V", description: "Key that switches between first and third person (Unity InputSystem key name).");
        EditorKey = c.CreateEntry("EditorKey", "F7", description: "Key that opens the character editor anywhere. Empty = only at the wardrobe in the village.");
        ThirdPerson = c.CreateEntry("ThirdPerson", false, description: "The view the game starts in; the view key updates it.");
        ShoulderRight = c.CreateEntry("ShoulderRight", 0.7f, description: "Third-person camera: metres to the right of the hero's head.");
        ShoulderUp = c.CreateEntry("ShoulderUp", 0.3f, description: "Third-person camera: metres above the hero's eyes.");
        Distance = c.CreateEntry("Distance", 2.5f, description: "Third-person camera: metres behind the hero. Walls pull it in.");
        Wardrobe = c.CreateEntry("Wardrobe", true, description: "Place the wardrobe (an armour rack that opens the character editor) by the shop gate in the village.");
        WardrobePosition = c.CreateEntry("WardrobePosition", "", description: "Wardrobe spot in the village as \"x, y, z, yaw\". Empty = by the shop gate.");
        DevCommands = c.CreateEntry("DevCommands", false, description: "Developer test commands from UserData/FellSellCharacter/dev_cmd.txt. Leave off.", is_hidden: true);
    }
}
