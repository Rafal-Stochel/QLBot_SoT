using ScriptsOfTribute;
using ScriptsOfTribute.AI;
using ScriptsOfTribute.Board;
using ScriptsOfTribute.Serializers;

using System;
using System.Text.Json;
using System.IO;
using System.Text;
using System.Reflection;

namespace Bots;

public class MyNewBot : AI
{
    private QL ql = new QL();

    private readonly SeededRandom rng = new(123);

    public override PatronId SelectPatron(List<PatronId> availablePatrons, int round)
        => availablePatrons.PickRandom(rng);

    public bool ShouldUseTreasury(SeededGameState game_state)
    {
        return false;
    }

    public override Move Play(GameState game_state, List<Move> possibleMoves, TimeSpan remainingTime)
    {
        Log("~~~~START~~~~");

        SeededGameState seeded_game_sate = game_state.ToSeededGameState(111);

        var buy_moves = possibleMoves.Where(m => m.Command == CommandEnum.BUY_CARD).ToList();
        var simple_moves = possibleMoves.Where(m => m.Command != CommandEnum.BUY_CARD && m.Command != CommandEnum.END_TURN).ToList();
        // var simple_moves = possibleMoves.Where(m => m.Command != CommandEnum.END_TURN).ToList();

        Move best_move = possibleMoves[0];
        int best_value = 0;


        if (simple_moves.Count() != 0)
        {
            foreach (var move in simple_moves)
            {
                int result = ql.RewardAfterApplyMove(seeded_game_sate, move);
                if (result > best_value)
                {
                    best_move = move;
                    best_value = result;
                }
            }
        }
        else if (buy_moves.Count() != 0)
        {
            best_move = ql.PickBuyMove(seeded_game_sate, buy_moves);
            ql.CalculateNewQValue(seeded_game_sate, best_move);
        }
        else
        {
            Log("end move = " + best_move.ToString());
            Log("~~~~END~~~~\n");
            return Move.EndTurn();
        }

        Log("Best move = " + best_move.ToString());
        Log("~~~~END~~~~\n");
    
        return best_move;
    }

    public override void GameEnd(EndGameState state, FullGameState? final_board_state)
    {
        ql.ResetComboCounters();

        ql.LogTMPStuff();

        ql.SaveQTableToFile();
    }
}
