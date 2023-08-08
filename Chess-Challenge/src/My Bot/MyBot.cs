using System;
using ChessChallenge.API;

namespace Chess_Challenge.My_Bot;

public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    
    private Move bestRootMove;
    record struct TTEntry(ulong key, Move move, int depth, int score, int bound);
    const int entries = (1 << 20), CheckmateValue = -100000;
    TTEntry[] tt = new TTEntry[entries];
    private int[,,] historyTable;

    private int timeLimit;

    private int bestEval;//#DEBUG
    
    public Move Think(Board _board, Timer _timer)
    {
        board = _board;
        timer = _timer;

        timeLimit = timer.MillisecondsRemaining / 30;
        //bestRootMove = Move.NullMove;
        historyTable = new int[2, 7, 64];

        for (int depth = 1;;)
        {
            int score = NegaMax(++depth, 0, CheckmateValue, -CheckmateValue, true);

            Console.WriteLine("PV was {0}{1} at depth {2} with an eval of {3} ({4})",//#DEBUG
                bestRootMove.StartSquare.Name,//#DEBUG
                bestRootMove.TargetSquare.Name,//#DEBUG
                depth, score.ToString(), bestEval.ToString());//#DEBUG
            
            if (timer.MillisecondsElapsedThisTurn > timeLimit) break;
            if (score > -CheckmateValue / 2) break;
        }

        return bestRootMove;
    }

    int NegaMax(int depth, int ply, int alpha, int beta, bool allowNullCheck)
    {
        bool isRoot = ply == 0, qSearch = depth <= 0, inCheck = board.IsInCheck();
        int bestScore = CheckmateValue * 2, turn = board.IsWhiteToMove ? 1 : 0;
        ulong key = board.ZobristKey;

        if (!isRoot && board.IsRepeatedPosition()) return 0;
        if (inCheck) depth++;
        
        TTEntry entry = tt[key % entries];
        
        // TT cutoffs
        if(!isRoot && entry.key == key && entry.depth >= depth && (
               entry.bound == 3 // exact score
               || entry.bound == 2 && entry.score >= beta // lower bound, fail high
               || entry.bound == 1 && entry.score <= alpha // upper bound, fail low
           )) return entry.score;

        if (qSearch)
        {
            bestScore = Evaluation();
            if (bestScore > beta) return beta;
            alpha = Math.Max(alpha, bestScore);
        }
        else if (beta - alpha == 1 && !inCheck)
        {
            int staticEval = Evaluation();
            if (staticEval - 85 * depth > beta) return staticEval - 85 * depth;

            if (allowNullCheck && depth >= 2)
            {
                board.TrySkipTurn();
                int score = -NegaMax(depth - 3 - depth / 6, ply + 1, -beta, -beta + 1, false);
                board.UndoSkipTurn();
                if (score >= beta) return score;
            }
        }

        Move[] moves = board.GetLegalMoves(qSearch && !inCheck);
        int[] scores = new int[moves.Length];

        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            scores[i] = move == entry.move ? 10000 :
            move.IsCapture ? 1000 * (int)move.CapturePieceType - (int)move.MovePieceType :
            historyTable[turn, (int)move.MovePieceType, move.TargetSquare.Index];
        }

        Move bestMove = Move.NullMove;
        int initialAlpha = alpha;

        for (int i = 0; i < moves.Length; i++)
        {
            if (timer.MillisecondsElapsedThisTurn > timeLimit) return -CheckmateValue;
            
            for (int j = 0; ++j < moves.Length;)
                if (scores[i] < scores[j])
                    (moves[i], moves[j], scores[i], scores[j]) =
                    (moves[j], moves[i], scores[j], scores[i]);

            Move move = moves[i];
            board.MakeMove(move);

            bool useFullSearch = qSearch || i == 0;
            int score = -NegaMax(depth - 1, ply + 1, useFullSearch ? -beta : -alpha - 1, -alpha,
                !useFullSearch || allowNullCheck);

            if (!useFullSearch && score > alpha)
                score = -NegaMax(depth - 1, ply + 1, -beta, -score, allowNullCheck);
            board.UndoMove(move);

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;

                if (isRoot) bestRootMove = move;
                if (isRoot) bestEval = score;//#DEBUG
                if (alpha >= beta)
                {
                    if (!qSearch && !move.IsCapture)
                        historyTable[turn, (int)move.MovePieceType, move.TargetSquare.Index] += depth * depth;
                    break;
                }
            }
        }

        if (!qSearch && moves.Length == 0) return inCheck ? CheckmateValue + ply : 0;
        tt[key % entries] = new TTEntry(
            key,
            bestMove,
            depth,
            bestScore,
            bestScore >= beta ? 2 : bestScore > initialAlpha ? 3 : 1
            );

        return bestScore;
    }
    
    int[] pieceVal = {0, 100, 310, 330, 500, 900, 10000 };
    int[] piecePhase = {0, 0, 1, 1, 2, 4, 0};
    ulong[] psts = {
        657614902731556116, 420894446315227099, 384592972471695068, 312245244820264086, 364876803783607569,366006824779723922,
        366006826859316500, 786039115310605588, 421220596516513823, 366011295806342421,366006826859316436, 366006896669578452,
        162218943720801556, 440575073001255824, 657087419459913430,402634039558223453, 347425219986941203, 365698755348489557,
        311382605788951956, 147850316371514514,329107007234708689, 402598430990222677, 402611905376114006, 329415149680141460,
        257053881053295759,291134268204721362, 492947507967247313, 367159395376767958, 384021229732455700, 384307098409076181,
        402035762391246293, 328847661003244824, 365712019230110867, 366002427738801364, 384307168185238804, 347996828560606484,
        329692156834174227, 365439338182165780, 386018218798040211, 456959123538409047, 347157285952386452, 365711880701965780,
        365997890021704981, 221896035722130452, 384289231362147538, 384307167128540502, 366006826859320596, 366006826876093716,
        366002360093332756, 366006824694793492, 347992428333053139, 457508666683233428, 329723156783776785, 329401687190893908,
        366002356855326100, 366288301819245844, 329978030930875600, 420621693221156179, 422042614449657239, 384602117564867863,
        419505151144195476, 366274972473194070, 329406075454444949, 275354286769374224, 366855645423297932, 329991151972070674,
        311105941360174354, 256772197720318995, 365993560693875923, 258219435335676691, 383730812414424149, 384601907111998612,
        401758895947998613, 420612834953622999, 402607438610388375, 329978099633296596, 67159620133902
    };
    
    int getPstVal(int psq) => (int)(((psts[psq / 10] >> (6 * (psq % 10))) & 63) - 20) * 8;
    
    int Evaluation() {
        int midGame = 0, endGame = 0, phase = 0;

        foreach(bool stm in new[] {true, false}) {
            for(var pieceType = 1; pieceType <= 6; pieceType++) {
                int piece = pieceType, ind;
                ulong mask = board.GetPieceBitboard((PieceType)pieceType, stm);
                while(mask != 0) {
                    phase += piecePhase[piece];
                    ind = 128 * (piece - 1) + BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ (stm ? 56 : 0);
                    midGame += getPstVal(ind) + pieceVal[piece];
                    endGame += getPstVal(ind + 64) + pieceVal[piece];
                }
            }

            midGame = -midGame;
            endGame = -endGame;
        }
        
        int indexDiff = Math.Abs(board.GetKingSquare(true).Index - board.GetKingSquare(false).Index);
        int distBetweenKings = indexDiff % 8 + indexDiff / 8;
        
        return (midGame * phase + endGame * (24 - phase) - distBetweenKings * (20 - phase) * 8) / 24 * (board.IsWhiteToMove ? 1 : -1);
    }
}