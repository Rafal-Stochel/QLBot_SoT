using ScriptsOfTribute;
using ScriptsOfTribute.AI;
using ScriptsOfTribute.Board;
using ScriptsOfTribute.Serializers;

using System;
using System.IO;
using System.Text;
using System.Reflection;

namespace Bots;


public class FilePaths
{
    private static string dir = Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.Parent.FullName;
    private static string botsPath = dir + @"\Bots\src\MyNewBot\";
    public static string qTablePath = botsPath + "QTABLE.txt";
    public static string tmpFile = botsPath + "tmp_file.txt";
    public static string errorFile = botsPath + "error_file.txt";
}

public class Consts
{
    public const int numberOfAllCards = 113;
    public const int maxStage = 3;
    public const int maxCombo = 3;

    // how big this should be in terms of QTable values?
    public const int infinityReward = 100;
}

public enum Stage
{
    Start = 0,
    Early = 1,
    Middle = 2,
    Late = 3,
}

public enum Combos
{
    None = 0,
    Small = 1,
    Medium = 2,
    Large = 3, // 3+
}

//?
public enum Key
{
    card_id = 0,
    player_stage = 1,
    enemy_stage = 2,
    combo = 3,
}