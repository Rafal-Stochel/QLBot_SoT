using ScriptsOfTribute;
using ScriptsOfTribute.AI;
using ScriptsOfTribute.Board;
using ScriptsOfTribute.Board.Cards;
using ScriptsOfTribute.Serializers;

namespace Bots;

public class HQL_BOT : AI
{
    private QL ql = new QL();

    private static Random random = new Random();
    private readonly SeededRandom rng = new SeededRandom((ulong)random.Next(1000));

    public override PatronId SelectPatron(List<PatronId> availablePatrons, int round)
        => availablePatrons.PickRandom(rng);

    public bool ShouldTradeCard(UniqueCard card)
    {
        return (card.Type == CardType.STARTER && card.Cost == 0) || card.Type == CardType.CURSE;
    }

    public bool ShouldUseTreasury(SeededGameState game_state, List<SimplePatronMove> patron_moves)
    {
        foreach (var move in patron_moves)
        {
            if (move.PatronId == PatronId.TREASURY)
            {
                var cards = game_state.CurrentPlayer.Hand.Concat(game_state.CurrentPlayer.Played);

                UniqueCard result = cards.First();
                foreach (var card in cards)
                {
                    if (ShouldTradeCard(card))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public UniqueCard PickCardForTreasury(SeededGameState game_state)
    {
        var cards = game_state.CurrentPlayer.Hand.Concat(game_state.CurrentPlayer.Played);

        UniqueCard result = cards.First();
        foreach (var card in cards)
        {
            if (ShouldTradeCard(card))
            {
                return card;
            }
        }

        return result;
    }

    public class SearchNode
    {
        public Move root_move;
        public SeededGameState current_sgs;
        public List<Move>? possible_moves;
        public int heuristic_score;

        public SearchNode(Move move, SeededGameState sgs, List<Move>? moves, int score)
        {
            root_move = move;
            current_sgs = sgs;
            possible_moves = moves;
            heuristic_score = score;
        }

    }

    private Move? FindNoChoiceMove(SeededGameState sgs, List<Move> possible_moves)
    {
        foreach (var move in possible_moves)
        {
            if (move is SimpleCardMove card)
            {
                if (NoChoiceMoves.ShouldPlay(card.Card.CommonId))
                {
                    return move;
                }
            }
        }

        return null;
    }

    public void PlaySimpleMovesOnNodeUntilChoice(SearchNode node)
    {
        if (node.possible_moves is null || node.current_sgs.PendingChoice is not null)
        {
            return;
        }

        var move = FindNoChoiceMove(node.current_sgs, node.possible_moves);
        while (move is not null)
        {
            var (new_sgs, new_possible_moves) = node.current_sgs.ApplyMove(move, (ulong)rng.Next());
            node.current_sgs = new_sgs;
            node.possible_moves = new_possible_moves;

            move = FindNoChoiceMove(node.current_sgs, node.possible_moves);
        }

        node.heuristic_score = ql.Heuristic(node.current_sgs);
    }

    private Tuple<List<SearchNode>, List<SearchNode>> GetPlayEndNodesFromMoves(SeededGameState sgs, List<Move> moves, Move initial_move)
    {
        List<SearchNode> play_nodes = new List<SearchNode>();
        List<SearchNode> end_nodes = new List<SearchNode>();

        foreach (var move in moves)
        {
            if (move.Command != CommandEnum.END_TURN)
            {
                var (new_state, new_moves) = sgs.ApplyMove(move, (ulong)rng.Next());
                play_nodes.Add(new SearchNode(initial_move, new_state, new_moves, ql.Heuristic(new_state)));
            }
            else
            {
                end_nodes.Add(new SearchNode(initial_move, sgs, null, ql.Heuristic(sgs)));
            }
        }

        return Tuple.Create(play_nodes, end_nodes);
    }

    private Move BestMoveFromSearch(SeededGameState initial_sgs, List<Move> possible_moves)
    {
        List<SearchNode> beam_nodes = new List<SearchNode>();
        List<SearchNode> end_nodes = new List<SearchNode>();
        foreach (var move in possible_moves)
        {
            if (move.Command != CommandEnum.END_TURN)
            {
                var (new_state, new_moves) = initial_sgs.ApplyMove(move, (ulong)rng.Next());
                beam_nodes.Add(new SearchNode(move, new_state, new_moves, ql.Heuristic(new_state)));
            }
            else
            {
                end_nodes.Add(new SearchNode(move, initial_sgs, null, ql.Heuristic(initial_sgs)));
            }
        }

        while (beam_nodes.Count > 0)
        {
            List<SearchNode> children_nodes = new List<SearchNode>();
            foreach (SearchNode node in beam_nodes)
            {
                var (single_child_nodes, new_end_nodes) = GetPlayEndNodesFromMoves(node.current_sgs, node.possible_moves, node.root_move);

                children_nodes.AddRange(single_child_nodes);
                end_nodes.AddRange(new_end_nodes);
            }

            beam_nodes = new List<SearchNode>(children_nodes);

            if (beam_nodes.Count > Consts.nodes_limit)
            {
                List<SearchNode> sorted_beam_nodes = beam_nodes.OrderBy(n => n.heuristic_score).ToList();
                beam_nodes = sorted_beam_nodes.Skip(beam_nodes.Count - Consts.nodes_limit).ToList();
            }

            foreach (var node in beam_nodes)
            {
                PlaySimpleMovesOnNodeUntilChoice(node);
            }
        }

        Move best_move = end_nodes[0].root_move;
        int best_score = end_nodes[0].heuristic_score;
        foreach (var node in end_nodes)
        {
            if (best_score < node.heuristic_score)
            {
                best_score = node.heuristic_score;
                best_move = node.root_move;
            }
        }

        return best_move;
    }

    public void HandleEndTurn(SeededGameState sgs)
    {
        ql.IncrementTurnCounter();
        ql.SaveGainedCards(sgs);
        ql.UpdateQValuesForPlayedCardsAtEndOfTurn(sgs);
    }

    public void HandleEndPlay(SeededGameState sgs, Move best_move)
    {
        ql.SavePlayedCardIfApplicable(best_move);
        ql.UpdateDeckCardsCounter(sgs);
    }

    public override Move Play(GameState game_state, List<Move> possibleMoves, TimeSpan remainingTime)
    {
        SeededGameState sgs = game_state.ToSeededGameState((ulong)rng.Next());

        Stage stage = ql.TransformGameStateToStages(sgs);

        if (possibleMoves.Count == 1 && possibleMoves[0].Command == CommandEnum.END_TURN)
        {
            HandleEndPlay(sgs, possibleMoves[0]);
            HandleEndTurn(sgs);
            return Move.EndTurn();
        }

        var action_agent_moves = possibleMoves.Where(m => m.Command == CommandEnum.PLAY_CARD ||
            m.Command == CommandEnum.ACTIVATE_AGENT).ToList();

        var buy_moves = possibleMoves.Where(m => m.Command == CommandEnum.BUY_CARD).ToList();
        var patron_moves = possibleMoves.Where(m => m.Command == CommandEnum.CALL_PATRON).ToList().ConvertAll(m => (SimplePatronMove)m);

        Move best_move = possibleMoves[0];

        if (action_agent_moves.Count != 0)
        {
            best_move = action_agent_moves[0];
            var no_choice_move = FindNoChoiceMove(sgs, action_agent_moves);
            if (no_choice_move is not null)
            {
                best_move = no_choice_move;
            }
        }
        // else if (buy_moves.Count != 0)
        // {
        //     // best_move = ql.PickBuyMove(sgs, buy_moves);
        // }
        else
        {
            for (int i = 0; i < buy_moves.Count; i++)
            {
                if (buy_moves[i] is SimpleCardMove buy_card)
                {
                    if (buy_card.Card.Type is CardType.CONTRACT_ACTION)
                    {
                        var (new_state, new_moves) = sgs.ApplyMove(buy_moves[i]);

                        var old_score = ql.Heuristic(sgs);
                        var new_score = ql.Heuristic(new_state);

                        if (old_score > new_score)
                        {
                            possibleMoves.RemoveAll(m => m == buy_moves[i]);
                        }
                    }
                }
            }

            for (int i = possibleMoves.Count - 1; i >= 0; i--)
            {
                if (possibleMoves[i] is SimplePatronMove patron)
                {
                    if (patron.PatronId == PatronId.DUKE_OF_CROWS && stage != Stage.Late && sgs.CurrentPlayer.Coins < 7)
                    {
                        possibleMoves.RemoveAt(i);
                        break;
                    }
                }
            }

            best_move = BestMoveFromSearch(sgs, possibleMoves);
        }

        HandleEndPlay(sgs, best_move);
        if (best_move.Command == CommandEnum.END_TURN)
        {
            HandleEndTurn(sgs);
        }

        return best_move;
    }

    public override void GameEnd(EndGameState state, FullGameState? final_board_state)
    {
        ql.ResetVariables();

        ql.SaveQTableToFile();
    }
}
