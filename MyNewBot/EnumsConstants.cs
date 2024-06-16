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
    private static string bots_path = dir + @"\Bots\src\MyNewBot\";
    public static string qtable_path = bots_path + "QTABLE.txt";
    public static string tmp_file = bots_path + "tmp_file.txt";
    public static string error_file = bots_path + "error_file.txt";
}

public class Consts
{
    public const int numberOfAllCards = 113;
    public const int stageRange = 4;
    public const int combosRange = 4;

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