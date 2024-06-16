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

public class FileStuff
{
    
}

public class QL
{
    private static double exploration_chance = 0.5;

    private static double learning_rate = 0.1;
    private static double discount_factor = 0.95;

    // action - card to buy, state = stages and combo
    // key = {0 - card id, 1 - player stage, 2 - enemy stage, 3 - combo for card's deck}
    private Dictionary<(int, int, int, int), double> q_table = new Dictionary<(int, int, int, int), double>();

    // PatronId range, Treasury spot added for safety now, delete later?
    private int[] comboCounters = new int[]{0, 0, 0, 0, 0, 0, 0, 0, 0};

    public QL()
    {
        using (var sw = new StreamWriter(FilePaths.error_file, append: false))
        {
            sw.WriteLine("start of game" + "\n");
            sw.WriteLine(DateTime.Now + "\n");
        }

        ReadQTableFromFile();
    }

    public void ReadQTableFromFile()
    {
        using (var streamReader = new StreamReader(FilePaths.qtable_path))
        {
            string raw_line;
            while ((raw_line = streamReader.ReadLine()) != null)
            {
                string key_value_str = String.Join("", raw_line.Split('(', ')', ' '));
                string[] key_value_arr = key_value_str.Split(':');
                int[] dict_keys = key_value_arr[0].Split(',').Select(int.Parse).ToArray();
                double dict_value = double.Parse(key_value_arr[1], System.Globalization.CultureInfo.InvariantCulture);

                q_table.Add((dict_keys[0], dict_keys[1], dict_keys[2], dict_keys[3]), dict_value);
            }
        }
    }

    public void SaveQTableToFile()
    {
        using (var sw = new StreamWriter(FilePaths.qtable_path))
        {
            foreach (var item in q_table)
            {
                sw.WriteLine(String.Join(", ", item.Key) + " : " + item.Value.ToString());
            }
        }
    }

    public void LogTMPStuff()
    {
        // log qtable to tmp_file
        // using (var sw = new StreamWriter(tmp_file, append: true))
        using (var sw = new StreamWriter(FilePaths.tmp_file, append: false))
        {
            // sw.WriteLine(q_table.Count());
            // foreach (var item in q_table)
            // {
            //     sw.WriteLine(String.Join(", ", item.Key) + " : " + item.Value.ToString());
            // }
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
                    using (var sw = new StreamWriter(FilePaths.error_file, append: true))
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

    public int[] GetQTableKey(SeededGameState seeded_game_state, Move move)
    {
        int[] key = new int[]{0, 0, 0, 0};

        SimpleCardMove card_move = (SimpleCardMove)move;

        key[0] = (int)card_move.Card.CommonId;
        
        // using (var sw = new StreamWriter(FilePaths.error_file, append: true))
        // {
        //     sw.WriteLine("GetQTableKey");
        //     sw.WriteLine("move = " + move.ToString());
        //     sw.WriteLine("common id = " + (int)card_move.Card.CommonId);
        //     sw.WriteLine(DateTime.Now + "\n");
        // }

        Stage[] stages = TransfromGameStateToStages(seeded_game_state);
        key[1] = (int)stages[0];
        key[2] = (int)stages[1];

        key[3] = comboCounters[(int)card_move.Card.Deck];

        return key;
    }

    public double MaxQValueFromNewState(int[] key)
    {
        double result = 0.0;

        for (int i = 0; i < Consts.numberOfAllCards; ++i)
        {
            double new_value = q_table[(i, key[1], key[2], key[3])];
            if (new_value > result)
            {
                result = new_value;
            }
        }

        return result;
    }

    public void CalculateNewQValue(SeededGameState seeded_game_state, Move move)
    {
        int[] key = GetQTableKey(seeded_game_state, move);

        double new_q_value = (1.0 - learning_rate) * q_table[(key[0], key[1], key[2], key[3])]
                             + learning_rate * (RewardAfterApplyMove(seeded_game_state, move)
                                                + discount_factor * MaxQValueFromNewState(key));

        // using (var sw = new StreamWriter(FilePaths.error_file, append: true))
        // {
        //     sw.WriteLine("move = " + move.ToString());
        //     sw.WriteLine("create key = " + string.Join(", ", key));
        //     sw.WriteLine("created value = " + new_q_value.ToString());
        //     sw.WriteLine("old value = " + q_table[(key[0], key[1], key[2], key[3])].ToString());
        //     sw.WriteLine(DateTime.Now + "\n");
        // }
        q_table[(key[0], key[1], key[2], key[3])] = new_q_value;
        // using (var sw = new StreamWriter(FilePaths.error_file, append: true))
        // {
        //     sw.WriteLine("new value = " + q_table[(key[0], key[1], key[2], key[3])].ToString());
        //     sw.WriteLine(DateTime.Now + "\n");
        // }
    }

    // Pick best by value move or explore other move
    // Return weakest move, if didn't pick any
    public Move PickBuyMove(SeededGameState seeded_game_state, List<Move> buy_moves)
    {
        Random random = new Random();

        SortedDictionary<double, Move> q_values_for_moves = new SortedDictionary<double, Move>();

        foreach (var move in buy_moves)
        {
            int[] key = GetQTableKey(seeded_game_state, move);
            double val = q_table[(key[0], key[1], key[2], key[3])];

            while (q_values_for_moves.ContainsKey(val))
            {
                val += random.NextDouble();
            }

            if (!q_values_for_moves.ContainsKey(val))
            {
                q_values_for_moves.Add(val, move);
            }
            else
            {
                using (var sw = new StreamWriter(FilePaths.error_file, append: true))
                {
                    // TODO: fix this, change collection for q_values_for_moves
                    sw.WriteLine("(still?) Duplicated key in PickBuyMove()");
                    sw.WriteLine(DateTime.Now + "\n");
                }
            }
        }
        Move result = q_values_for_moves.Values.Last();
        var card_move = (SimpleCardMove)result;
        comboCounters[(int)card_move.Card.Deck]++;

        foreach (var item in q_values_for_moves.Reverse())
        {
            if (random.NextDouble() < exploration_chance)
            {
                // using (var sw = new StreamWriter(FilePaths.error_file, append: true))
                // {
                //     sw.WriteLine("picked\n");
                // }
                return item.Value;
            }
            else
            {
                // using (var sw = new StreamWriter(FilePaths.error_file, append: true))
                // {
                //     sw.WriteLine("didnt pick move = " + item.Value.ToString() + "\n");
                // }
            }
        }

        return result;
    }
}