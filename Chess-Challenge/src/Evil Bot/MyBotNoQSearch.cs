using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ChessChallenge.Example
{
    // A simple bot that can spot mate in one, and always captures the most valuable piece it can.
    // Plays randomly otherwise.
    public class MyBotNoQSearch : IChessBot
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

    public MyBotNoQSearch()
    {
        tpTable = new Transposition[tpMask + 1];
    }
    
    private int maxDepth = 20;
    private int stopTime = 3000;
    private Boolean stopSearch;

    private int nodesChecked;
    
    public Move Think(Board _board, Timer _timer)
    {
        board = _board;
        timer = _timer;

        nodesChecked = 0;
        stopTime = timer.MillisecondsRemaining - 3000 + (board.IsInCheck() ? 1000 : 0);
        stopSearch = false;
        
        Transposition bestMove = tpTable[board.ZobristKey & tpMask];
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            PVS(depth, int.MinValue, int.MaxValue, 0);
            bestMove = tpTable[board.ZobristKey & tpMask];
            
            if (stopSearch) break;
        }
        
        // Console.WriteLine("Current Fen: " + board.GetFenString());
        // Console.WriteLine("Best Move: " + bestMove.move + " | Eval: " + bestMove.evaluation + " at depth " + bestMove.depth + " | Nodes Checked: " + nodesChecked);
        
        if (bestMove.flag == INVALID) return board.GetLegalMoves()[0];
        return bestMove.move;
    }

    void checkTime()
    {
        if (timer.MillisecondsRemaining < stopTime) stopSearch = true;
    }
    
    // No TT = 10.3 | Depth 5
    // Plain TT = 4.8 | Depth 5
    // Internal Iteration = 4.6 | Depth 7
    // Time Limiting
    
    float PVS(int depth, float alpha, float beta, int currentExtensions)
    {
        nodesChecked++;
        if (depth == 0) return Evaluate();
        if (board.IsRepeatedPosition() || board.IsInsufficientMaterial() || board.FiftyMoveCounter >= 50) return 0f;
        if (board.IsInCheckmate()) return -1f;

        float startingAlpha = alpha;
        
        ref Transposition transposition = ref tpTable[board.ZobristKey & tpMask];
        if (transposition.zobristHash == board.ZobristKey && transposition.flag != INVALID && transposition.depth >= depth)
        {
            if(transposition.flag == EXACT) return transposition.evaluation;
            if(transposition.flag == LOWERBOUND) alpha = Math.Max(alpha, transposition.evaluation);
            else if(transposition.flag == UPPERBOUND) beta = Math.Min(beta, transposition.evaluation);
            if(alpha >= beta) return transposition.evaluation;
        }
        
        Move[] legalMoves = board.GetLegalMoves();
        if (legalMoves.Length <= 1) return Evaluate();
        OrderMoves(ref legalMoves);
        
        Move bestMove = legalMoves[0];
        float bestEval = float.MinValue;
        int movesChecked = 0;
        foreach (Move move in legalMoves)
        {
            board.MakeMove(move);
            int extension = (move.IsPromotion || board.IsInCheck()) && currentExtensions < 16 ? 1 : 0;
            
            float score;
            if (movesChecked == 0) score = -PVS(depth - 1 + extension, -beta, -alpha, currentExtensions + extension);
            else if (movesChecked >= 5 && extension == 0) score = -PVS(depth / 2, -alpha - 1, -alpha, currentExtensions + extension);
            else score = -PVS(depth - 1 + extension, -alpha - 1, -alpha, currentExtensions + extension);
            
            if (alpha < score && score < beta) score = -PVS(depth - 1 + extension, -beta, -score, currentExtensions + extension);
            
            board.UndoMove(move);

            if (score > bestEval)
            {
                bestEval = score;
                bestMove = move;
            }

            movesChecked++;
            alpha = Math.Max(alpha, score);
            
            checkTime();
            if (stopSearch) break;
            if (alpha > beta) break;
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
        if (board.IsRepeatedPosition() || board.IsInsufficientMaterial() || board.FiftyMoveCounter >= 50) return 0f;
        if (board.IsInCheckmate()) return -1f;
        
        float score = 0;

        //Check the number of pawns you currently have in play
        int numPawns = 8 - board.GetPieceList(PieceType.Pawn, board.IsWhiteToMove).Count;
        int numBishops = board.GetPieceList(PieceType.Bishop, board.IsWhiteToMove).Count;

        int gamePhase = GetGamePhase();
            
        // Piece values: null, pawn, knight, bishop, rook, queen, king
        int[] pieceWeights =
        {
            0,
            200 + numPawns*25 + gamePhase,
            300,
            400 + (100 * numBishops),
            500 + gamePhase,
            1100,
            99999
        };
            
        for (int i = 1; i < 6; i++)
        {
            ulong whitePieces = board.GetPieceBitboard((PieceType)i, true);
            ulong blackPieces = board.GetPieceBitboard((PieceType)i, false);
            score += pieceWeights[i - 1] * (SetBitCount(whitePieces) - SetBitCount(blackPieces));
            score -= 15 * (SetBitCount(whitePieces & bordermagic) - SetBitCount(blackPieces & bordermagic));
        }

        // //0x0101010101010101
        PieceList rooks = board.GetPieceList(PieceType.Rook, board.IsWhiteToMove);
            
        for (int i = 0; i < rooks.Count; i++)
            score += SetBitCount((ulong)(0x0101010101010101 << rooks[i].Square.File) & board.GetPieceBitboard(PieceType.Pawn, !board.IsWhiteToMove)) * 5;

        //score += board.GetLegalMoves().Length;

        if (!board.IsWhiteToMove) score = -score;

        return 0.9f * MathF.Tanh(score / 250);
    }
        
    int GetGamePhase()
    {
        return ((54 - (GetBothSidePieceCount(PieceType.Pawn) * 1
                       + GetBothSidePieceCount(PieceType.Knight) * 2
                       + GetBothSidePieceCount(PieceType.Bishop) * 2
                       + GetBothSidePieceCount(PieceType.Rook) * 3
                       + GetBothSidePieceCount(PieceType.Queen) * 5)) * 256 + (54 / 2)) / 54;
    }
        
    int GetBothSidePieceCount(PieceType pieceType)
    { return BitboardHelper.GetNumberOfSetBits(board.GetPieceBitboard(pieceType, true) | board.GetPieceBitboard(pieceType, false)); }

    int SetBitCount(ulong bitBoard) { return BitboardHelper.GetNumberOfSetBits(bitBoard); }
    
    private void OrderMoves(ref Move[] moves)
    {
        List<Tuple<Move, int>> orderedMoves = new();
        foreach(Move move in moves) orderedMoves.Add(new Tuple<Move, int>(move, GetMovePriority(move)));
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
        return priority;
    }
}
}