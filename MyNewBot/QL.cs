using ScriptsOfTribute;
using ScriptsOfTribute.AI;
using ScriptsOfTribute.Board;
using ScriptsOfTribute.Serializers;

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Reflection;

namespace Bots;

using qkey = Tuple<int, int, int, int>;

public class FileStuff
{
    
}

public class QL
{

    private static double explorationChance = 0.5;

    private static double learningRate = 0.1;
    private static double discountFactor = 0.95;

    // action - card to buy, state = stages and combo
    // key = {0 - card id, 1 - player stage, 2 - enemy stage, 3 - combo for card's deck}
    private Dictionary<qkey, double> qTable = new Dictionary<qkey, double>();

    // PatronId range, Treasury spot added for safety now, delete later?
    private int[] comboCounters = new int[]{0, 0, 0, 0, 0, 0, 0, 0, 0};

    public QL()
    {
        using (var sw = new StreamWriter(FilePaths.errorFile, append: true))
        {
            sw.WriteLine("Start of game");
            sw.WriteLine(DateTime.Now + "\n\n");
        }

        using (var sw = new StreamWriter(FilePaths.tmpFile, append: false))
        {
            sw.WriteLine("Start of game");
            sw.WriteLine(DateTime.Now + "\n");
        }

        ReadQTableFromFile();
    }

    public void ReadQTableFromFile()
    {
        using (var streamReader = new StreamReader(FilePaths.qTablePath))
        {
            string raw_line;
            while ((raw_line = streamReader.ReadLine()) != null)
            {
                string key_value_str = String.Join("", raw_line.Split('(', ')', ' '));
                string[] key_value_arr = key_value_str.Split(':');
                int[] dict_keys = key_value_arr[0].Split(',').Select(int.Parse).ToArray();
                double dict_value = double.Parse(key_value_arr[1].Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);

                qTable.Add(Tuple.Create(dict_keys[0], dict_keys[1], dict_keys[2], dict_keys[3]), dict_value);
                if (dict_value > 1000)
                {
                    WriteLineToErrorFile($"str = {key_value_arr[1].Replace(',', '.')}, double = {dict_value}");
                }
            }
        }
    }

    public void SaveQTableToFile()
    {
        using (var sw = new StreamWriter(FilePaths.qTablePath))
        {
            foreach (var item in qTable)
            {
                sw.WriteLine(String.Join(", ", item.Key) + " : " + item.Value.ToString());
            }
        }
    }

    public void WriteLineToErrorFile(string msg)
    {
        using (var sw = new StreamWriter(FilePaths.errorFile, append: true))
        {
            sw.WriteLine(msg);
        }
    }

    public void WriteLineToTmpFile(string msg)
    {
        using (var sw = new StreamWriter(FilePaths.tmpFile, append: true))
        {
            sw.WriteLine(msg);
        }
    }

    public double TryToGetQValue(qkey key)
    {
        double q_value = 0.0;
        if (qTable.TryGetValue(key, out q_value))
        {
            return q_value;
        }
        else
        {
            using (var sw = new StreamWriter(FilePaths.errorFile, append: true))
            {
                sw.WriteLine("q_table doesn't contain key: {0}, {1}, {2}, {3}", key.Item1, key.Item2, key.Item3, key.Item4);
                sw.WriteLine(DateTime.Now + "\n");
            }

            return 0.0;
        }
    }

    public Stage[] TransfromGameStateToStages(SeededGameState seeded_game_state)
    {
        Func<int, Stage> FindStage = x =>
        {
            switch (x)
            {
                case >= 0 and < 10:
                    return Stage.Start;
                case >= 10 and < 20:
                    return Stage.Early;
                case >= 20 and < 30:
                    return Stage.Middle;
                case >= 30:
                    return Stage.Late;
                default:
                    using (var sw = new StreamWriter(FilePaths.errorFile, append: true))
                    {
                        sw.WriteLine("Unexpected prestige value in TransfromGameStateToGrade() = " + x.ToString());
                        sw.WriteLine(DateTime.Now + "\n");
                    }
                    return Stage.Middle;
            }
        };
        
        Stage player_stage = FindStage(seeded_game_state.CurrentPlayer.Prestige);
        Stage enemy_stage = FindStage(seeded_game_state.EnemyPlayer.Prestige);

        return new Stage[]{player_stage, enemy_stage};
    }

    public void IncrementComboCounter(int patron_id)
    {
        comboCounters[patron_id]++;
        if (comboCounters[patron_id] > Consts.maxCombo)
        {
            comboCounters[patron_id] = Consts.maxCombo;
        }
    }

    public void ResetComboCounters()
    {
        comboCounters = new int[]{0, 0, 0, 0, 0, 0, 0, 0, 0};
    }

    public int RewardAfterApplyMove(SeededGameState seeded_game_state, Move move)
    {
        int result = 0;
        var (new_state, new_possible_moves) = seeded_game_state.ApplyMove(move);

        if (new_state.GameEndState?.Winner == new_state.CurrentPlayer.PlayerID)
        {
            return Consts.infinityReward;
        }

        result += new_state.CurrentPlayer.Prestige + new_state.CurrentPlayer.Power + new_state.CurrentPlayer.Coins;

        return result;
    }

    public qkey ConstructQTableKey(SeededGameState seeded_game_state, Move move)
    {
        SimpleCardMove card_move = (SimpleCardMove)move;

        Stage[] stages = TransfromGameStateToStages(seeded_game_state);

        return Tuple.Create((int)card_move.Card.CommonId, (int)stages[0], (int)stages[1], comboCounters[(int)card_move.Card.Deck]);
    }

    public double MaxQValueFromNewState(qkey key)
    {
        double result = 0.0;

        for (int i = 0; i < Consts.numberOfAllCards; ++i)
        {
            double new_value = TryToGetQValue(Tuple.Create(i, key.Item2, key.Item3, key.Item4));
            WriteLineToTmpFile($"{i}, {key.Item2}, {key.Item3}, {key.Item4}");
            WriteLineToTmpFile($"new_value = {new_value}");
            if (new_value > result)
            {
                result = new_value;
            }
        }

        return result;
    }

    public void CalculateNewQValue(SeededGameState seeded_game_state, Move move)
    {
        qkey key = ConstructQTableKey(seeded_game_state, move);

        double q_value = TryToGetQValue(key);

        WriteLineToTmpFile($"old q_value = {q_value}");
        WriteLineToTmpFile($"(1.0 - learningRate) * q_value = {(1.0 - learningRate) * q_value}");
        WriteLineToTmpFile($"RewardAfterApplyMove = {RewardAfterApplyMove(seeded_game_state, move)}");
        WriteLineToTmpFile($"MaxQValueFromNewState = {MaxQValueFromNewState(key)}");

        double new_q_value = (1.0 - learningRate) * q_value
                            + learningRate * (RewardAfterApplyMove(seeded_game_state, move)
                                                + discountFactor * MaxQValueFromNewState(key));

        WriteLineToTmpFile($"result = {new_q_value}\n");

        qTable[key] = new_q_value;
    }

    // Pick best by value move or explore other move
    // Return weakest move, if didn't pick any
    public Move PickBuyMove(SeededGameState seeded_game_state, List<Move> buy_moves)
    {
        Random random = new Random();
        
        // WriteLineToTmpFile("buy moves count = " + buy_moves.Count.ToString());

        List<Tuple<int, double>> moves_values = new List<Tuple<int, double>>();
        for (int i = 0; i < buy_moves.Count; ++i)
        {
            qkey key = ConstructQTableKey(seeded_game_state, buy_moves[i]);
            double q_value = TryToGetQValue(key);
            moves_values.Add(Tuple.Create(i, q_value));
        }

        // Sort in descending order
        moves_values.Sort((x, y) => y.Item2.CompareTo(x.Item2));

        // foreach (var item in moves_values)
        // {
        //     WriteLineToTmpFile(item.Item1 + " , " + buy_moves[item.Item1] + " , " + item.Item2);
        // }

        Move result = buy_moves[moves_values.Last().Item1];
        var card_move = (SimpleCardMove)result;
        IncrementComboCounter((int)card_move.Card.Deck);

        foreach (var item in moves_values)
        {
            if (random.NextDouble() < explorationChance)
            {
                return buy_moves[item.Item1];
            }
        }

        // WriteLineToTmpFile("didnt pick any pickbuymove, return last");
        return result;
    }
}