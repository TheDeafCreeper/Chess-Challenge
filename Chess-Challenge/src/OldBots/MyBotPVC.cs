#define LOGGING

using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;

namespace Chess_Challenge.My_Bot;

public class MyBotPVC : IChessBot
{
    Board board;
    Timer timer;
    
    private Transposition[] tpTable;
    private ulong tpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
    private const byte INVALID = 0, EXACT = 1, LOWERBOUND = 2, UPPERBOUND = 3;
    
    public struct Transposition
    {
        public ulong zobristHash;
        public Move move;
        public float evaluation;
        public int depth;
        public byte flag;
    };

    public MyBotPVC() { tpTable = new Transposition[tpMask + 1]; }
    
    private int maxDepth;
    private Boolean stopSearch;

    private int nodesChecked; //#DEBUG
    private int qNodesChecked; //#DEBUG

    private double maxTimeTaken = 10000;
    
    private Dictionary<int, Move[]> killerMoves = new();
    
    public Move Think(Board _board, Timer _timer)
    {
        board = _board;
        timer = _timer;

        nodesChecked = 0; //#DEBUG
        qNodesChecked = 0; //#DEBUG
        
        stopSearch = false;
        killerMoves.Remove(board.PlyCount);
        
        Transposition bestMove = tpTable[board.ZobristKey & tpMask];
        int depthStartTime;

        if (timer.MillisecondsRemaining < 250) maxDepth = 1;
        else if (timer.MillisecondsRemaining < 1000) maxDepth = 3;
        else maxDepth = 20;
        
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            int msRemaining = timer.MillisecondsRemaining, opponentMSRemaining = timer.OpponentMillisecondsRemaining;
            
            depthStartTime = timer.MillisecondsElapsedThisTurn;//#DEBUG
            maxTimeTaken = timer.IncrementMilliseconds * .9 + Min(
                msRemaining - 10, 
                Min(10000,
                    msRemaining / 35 + (msRemaining > opponentMSRemaining ? Min(msRemaining - opponentMSRemaining,500) : -500)
                    )
                );

            PVS(depth: depth);
            bestMove = tpTable[board.ZobristKey & tpMask];
            
            //Console.WriteLine("Depth " + depth + " took " + (timer.MillisecondsElapsedThisTurn - depthStartTime) + "ms | Was Allotted " + Math.Min(maxTimeTaken - timer.MillisecondsElapsedThisTurn, maxTimeTaken) + "ms");//#DEBUG

            if (shouldStop()) break;
        }
        
        Console.WriteLine("Current Fen: " + board.GetFenString()); //#DEBUG
        Console.WriteLine("Best Move: " + bestMove.move + " | Eval: " + (bestMove.evaluation/200) + " at depth " + bestMove.depth);//#DEBUG
        
        float secondsPassed = timer.MillisecondsElapsedThisTurn / 1000f;//#DEBUG
        Console.WriteLine("Time Taken: " + secondsPassed + " seconds | Nodes Checked: " + nodesChecked + " | QNodes Checked: " + qNodesChecked + " | Nodes Per Second: " + (nodesChecked + qNodesChecked) / secondsPassed);//#DEBUG
        Console.WriteLine("================================");//#DEBUG
        
        Move[] moves = board.GetLegalMoves();
        if (!moves.Contains(bestMove.move)) return moves[0];
        if (bestMove.flag == INVALID) return moves[0];
        return bestMove.move;
    }

    void addKillerMove(Move move)
    {
        Move[] currentKillerMoves = killerMoves.GetValueOrDefault(board.PlyCount, new Move[3]);
        currentKillerMoves.Prepend(move);
        killerMoves[board.PlyCount] = currentKillerMoves;
    }

    Boolean shouldStop() => timer.MillisecondsElapsedThisTurn > maxTimeTaken && maxDepth > 3;

    // Piece values: null, pawn, knight, bishop, rook, queen, king
    int[] pieceWeights = { 0, 200, 300, 400, 500, 1100, 99999 };

    float PVS(float alpha = float.MinValue, float beta = float.MaxValue, int depth = 0, int currentExtensions = 0)
    {
        nodesChecked++; //#DEBUG
        if (depth == 0) return Evaluate();
        if (board.IsDraw()) return 0f;
        if (board.IsInCheckmate()) return float.MinValue - board.PlyCount;
        
        ref Transposition transposition = ref tpTable[board.ZobristKey & tpMask];
        if (transposition.zobristHash == board.ZobristKey && transposition.flag != INVALID && transposition.depth >= depth)
        {
            if(transposition.flag == EXACT) return transposition.evaluation;
            if(transposition.flag == LOWERBOUND) alpha = Max(alpha, transposition.evaluation);
            else if(transposition.flag == UPPERBOUND) beta = Min(beta, transposition.evaluation);
            if(alpha >= beta)
            {
                addKillerMove(transposition.move);
                return transposition.evaluation;
            }
        }
        
        float startingAlpha = alpha;
        
        Move[] legalMoves = board.GetLegalMoves();
        OrderMoves(ref legalMoves);
        
        Move bestMove = legalMoves[0];
        float bestEval = float.MinValue;
        int movesChecked = 0;
        foreach (Move move in legalMoves)
        {
            if (shouldStop()) return 0;
            board.MakeMove(move);
            int extension = (move.IsPromotion || board.IsInCheck()) && currentExtensions < 16 ? 1 : 0;
            
            int newDepth = depth - 1 + extension;
            currentExtensions += extension;
            
            float score;
            if (movesChecked++ == 0) score = -PVS(-beta, -alpha, newDepth, currentExtensions);
            else if (movesChecked >= 5 && extension == 0) score = -PVS(-alpha - 1, -alpha, depth / 2, currentExtensions);
            else score = -PVS(-alpha - 1, -alpha, depth - 1 + extension, currentExtensions);
                
            if (alpha < score && score < beta) score = -PVS(-beta, -score, newDepth, currentExtensions);
            
            board.UndoMove(move);

            if (score > bestEval)
            {
                bestEval = score;
                bestMove = move;
            }

            alpha = Max(alpha, score);
            
            if (alpha >= beta)
            {
                addKillerMove(bestMove);
                break;
            }
        }
        
        transposition.evaluation = bestEval;
        transposition.zobristHash = board.ZobristKey;
        transposition.move = bestMove;
        
        if(bestEval < startingAlpha) transposition.flag = UPPERBOUND;
        else if(bestEval >= beta) transposition.flag = LOWERBOUND;
        else transposition.flag = EXACT;

        transposition.depth = depth;

        return alpha;
    }
    
    ulong bordermagic = 18411139144890810879;
    
    float Evaluate()
    {
        if (board.IsDraw()) return 0f;
        if (board.IsInCheckmate()) return float.MinValue - board.PlyCount;
        
        float score = 0;
        
        int numPawns = 8 - board.GetPieceList(PieceType.Pawn, board.IsWhiteToMove).Count;
        int numBishops = board.GetPieceList(PieceType.Bishop, board.IsWhiteToMove).Count;

        for (int i = 1; i < 6; i++)
        {
            ulong whitePieces = board.GetPieceBitboard((PieceType)i, true);
            ulong blackPieces = board.GetPieceBitboard((PieceType)i, false);
            score += pieceWeights[i] * (SetBitCount(whitePieces) - SetBitCount(blackPieces));
            
            if (i == 1) score += numPawns * 25 + board.PlyCount;
            else if (i == 3) score += (50 * numBishops);
            else if (i == 4) score += board.PlyCount;
            
            score -= 15 * (SetBitCount(whitePieces & bordermagic) - SetBitCount(blackPieces & bordermagic));
        }

        if (!board.IsWhiteToMove) score = -score;

        return score;
    }
    
    int SetBitCount(ulong bitBoard) => BitboardHelper.GetNumberOfSetBits(bitBoard);
    
    private void OrderMoves(ref Move[] moves)
    {
        List<(Move, int)> orderedMoves = new();
        foreach(Move move in moves) orderedMoves.Add(new (move, GetMovePriority(move)));
        orderedMoves.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        for(int i = 0; i < moves.Length; i++) moves[i] = orderedMoves[i].Item1;
    }
    
    private readonly int[] mostValuableVictimValues = { 0, 10, 20, 30, 40, 50, 0 };
    private readonly int[] leastValuableAttackerValues = { 0, 5, 4, 3, 2, 1, 0 };
    
    private int GetMovePriority(Move move)
    {
        int priority = 0;
        Transposition tp = tpTable[board.ZobristKey & tpMask];
        if(tp.move == move && tp.zobristHash == board.ZobristKey) priority += 1000;
        if (move.IsCapture) priority = mostValuableVictimValues[(int)move.CapturePieceType] + leastValuableAttackerValues[(int)move.MovePieceType];
        if (killerMoves.GetValueOrDefault(board.PlyCount, new Move[3]).Contains(move)) priority += 500;
        return priority;
    }

    float Max(float a, float b) => Math.Max(a, b);
    float Min(float a, float b) => Math.Min(a, b);
}