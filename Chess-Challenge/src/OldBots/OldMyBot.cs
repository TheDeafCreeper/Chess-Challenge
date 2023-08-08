using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Chess_Challenge.My_Bot;
using ChessChallenge.API;
using ChessChallenge.Application.APIHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public class OldMyBot : IChessBot
{
    Board board;

    // Transposition Stuff
    private Transposition[] tpTable;
    private ulong tpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
    private const byte INVALID = 0, EXACT = 1, LOWERBOUND = 2, UPPERBOUND = 3;
    
    // Use tpTable[zobristKey & tpMask] to index.

    public struct Transposition
    {
        public ulong zobristHash;
        public Move move;
        public float evaluation;
        public int depth;
        public byte flag;
    };

    public OldMyBot()
    {
        tpTable = new Transposition[tpMask + 1];
    }
    
    private int maxDepth = 20;
    private int maxTime = 3000;
    
    public Move Think(Board startBoard, Timer timer)
    {
        board = startBoard;

        Transposition bestMove = tpTable[board.ZobristKey & tpMask];
        if (timer.MillisecondsRemaining < 30000) maxDepth = 4;
        if (timer.MillisecondsRemaining < 10000) maxDepth = 3;
        if (timer.MillisecondsRemaining < 2000) maxDepth = 2;
        if (timer.MillisecondsRemaining < 500) maxDepth = 1;
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            NegaMax(depth, float.MinValue, float.MaxValue, 0);
            bestMove = tpTable[board.ZobristKey & tpMask];
            
            Console.WriteLine("Best " + bestMove.move + "; Depth " + depth + ". Eval: " + bestMove.evaluation);
            
            int currentThinkTime = timer.MillisecondsElapsedThisTurn;
            if (maxTime - currentThinkTime < currentThinkTime * 3) break;
        }
        
        Console.WriteLine("Fen String: " + board.GetFenString());
        Console.WriteLine("Best " + bestMove.move + ". Eval: " + bestMove.evaluation + " at depth " + bestMove.depth);
        
        return bestMove.move;
    }

    float NegaMax(int depth, float alpha, float beta, int totalExtensions)
    {
        if (depth <= 0) return QNegaMax(alpha, beta);
        
        float bestEval = float.MinValue;
        float startingAlpha = alpha;
        
        ref Transposition transposition = ref tpTable[board.ZobristKey & tpMask];
        if (transposition.zobristHash == board.ZobristKey && transposition.flag != INVALID && transposition.depth >= depth)
        {
            if(transposition.flag == EXACT) return transposition.evaluation;
            if(transposition.flag == LOWERBOUND) alpha = Math.Max(alpha, transposition.evaluation);
            else if(transposition.flag == UPPERBOUND) beta = Math.Min(beta, transposition.evaluation);
            if(alpha >= beta) return transposition.evaluation;
        }
        
        if (board.IsDraw()) return -10000 + board.PlyCount;
        if (board.IsInCheckmate()) return int.MinValue + board.PlyCount;
        
        Move[] moves = board.GetLegalMoves();
        if (moves.Length == 0) return int.MinValue + board.PlyCount;
        
        OrderMoves(ref moves);
        
        Move bestMove = moves[0];
        
        int movesSearched = 0;
        foreach (Move move in moves)
        {
            float moveEval = 0;
            Boolean needsFullSearch = true;
            int extension = (board.IsInCheck() && totalExtensions < 16) ? 1 : 0;
            if (movesSearched >= 3 && extension == 0 && depth >= 3 && !move.IsCapture)
            {
                board.MakeMove(move);
                moveEval = -NegaMax(depth / 2, -beta, -alpha, totalExtensions + extension);
                board.UndoMove(move);
                needsFullSearch = moveEval > alpha;
            }

            if (needsFullSearch)
            {
                board.MakeMove(move);
                moveEval = -NegaMax(depth - 1 + extension, -beta, -alpha, totalExtensions + extension);
                board.UndoMove(move);
            }

            movesSearched++;
            
            if (bestEval < moveEval)
            {
                bestMove = move;
                bestEval = moveEval;
            }

            alpha = Math.Max(moveEval, alpha);
            if (alpha > beta) break;
        }

        transposition.evaluation = bestEval;
        transposition.zobristHash = board.ZobristKey;
        transposition.move = bestMove;
        
        if(bestEval < startingAlpha) transposition.flag = UPPERBOUND;
        else if(bestEval >= beta) transposition.flag = LOWERBOUND;
        else transposition.flag = EXACT;

        transposition.depth = depth;
        return bestEval;
    }

    float QNegaMax(float alpha, float beta)
    {
        Move[] moves = board.GetLegalMoves(!board.IsInCheck());

        if (moves.Length == 0) return evaluation();
        
        Transposition transposition = tpTable[board.ZobristKey & tpMask];
        if (transposition.zobristHash == board.ZobristKey && transposition.flag != INVALID && transposition.depth >= 0)
        {
            if(transposition.flag == EXACT) return transposition.evaluation;
            if(transposition.flag == LOWERBOUND) alpha = Math.Max(alpha, transposition.evaluation);
            else if(transposition.flag == UPPERBOUND) beta = Math.Min(beta, transposition.evaluation);
            if(alpha >= beta) return transposition.evaluation;
        }
        
        alpha = Math.Max(evaluation(), alpha);
        if (alpha >= beta) return beta;
        
        OrderMoves(ref moves);
        
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            float moveEval = -QNegaMax(-beta, -alpha);
            board.UndoMove(move);

            alpha = Math.Max(moveEval, alpha);
            if (alpha > beta) break;
        }

        return alpha;
    }
    
    
    
    private void OrderMoves(ref Move[] moves)
    {
        List<Tuple<Move, float>> orderedMoves = new();
        foreach(Move m in moves) orderedMoves.Add(new Tuple<Move, float>(m, GetMovePriority(m)));
        orderedMoves.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        for(int i = 0; i < moves.Length; i++) moves[i] = orderedMoves[i].Item1;
    }
    
    private readonly float[] mostValuableValues = { 0f, .1f, .20f, .30f, .40f, .50f, 0f };
    private readonly float[] leastValuableValues = { 0f, .05f, .04f, .03f, .02f, .01f, 0f };
    private float GetMovePriority(Move move)
    {
        float priority = 0f;
        if (move.IsCapture) priority = mostValuableValues[(int)move.CapturePieceType] + leastValuableValues[(int)move.MovePieceType];
        if (move.IsPromotion) priority += .5f;

        return priority;
    }
    
    int[] pieceWeights = {100,280,320,500,900};
    ulong bordermagic = 18411139144890810879;
    
    float evaluation()
    {
        if (board.IsDraw()) return -5f;
        if (board.IsInCheckmate()) return -10f;
        
        float score = 0;

        for (int i = 1; i < 6; i++)
        {
            ulong whitePieces = board.GetPieceBitboard((PieceType)i, true);
            ulong blackPieces = board.GetPieceBitboard((PieceType)i, false);
            score += pieceWeights[i - 1] * (BitOperations.PopCount(whitePieces) - BitOperations.PopCount(blackPieces));
            score -= 15 * (BitOperations.PopCount(whitePieces & bordermagic) - BitOperations.PopCount(blackPieces & bordermagic));
        }

        score += KingSafety(true) - KingSafety(false);
        
        if (!board.IsWhiteToMove) score = -score;
        
        if (GetGamePhase() > 200 && score > 0 && DistanceBetweenKings() > 3) score += DistanceBetweenKings() * -100;

        return 0.9f * MathF.Tanh(score / 250);
    }
    
    int KingSafety(Boolean white)
    {
        Square kingSquare = board.GetKingSquare(white);

        int safetyScore = BitsSet((ulong)0xffff << (kingSquare.Rank - (white ? 0 : 1)) * 8
                                        & GetAdjacentFiles(board.GetPiece(kingSquare))
                                        & board.GetPieceBitboard(PieceType.Pawn, white)) * 4;

        safetyScore -= BitsSet(BitboardHelper.GetSliderAttacks(PieceType.Queen,
            board.GetKingSquare(white), ulong.MinValue) & board.GetPieceBitboard(PieceType.Pawn, white));

        ulong enemyBitBoard = white ? board.BlackPiecesBitboard : board.WhitePiecesBitboard;

        if (board.PlyCount < 15) return safetyScore / 10;
        if (GetGamePhase() > 130) return 0;
        if (kingSquare.File < 3) return safetyScore * 16 - BitsSet(0xf0f0f0f0f0f0f0f & enemyBitBoard);
        if (kingSquare.File > 5) return safetyScore * 16 - BitsSet(0xf0f0f0f0f0f0f0f0 & enemyBitBoard);
        return safetyScore * -2;
    }

    int BitsSet(ulong bitboard)
    {
        return BitboardHelper.GetNumberOfSetBits(bitboard);
    }

    ulong GetAdjacentFiles(Piece piece)
    {
        ulong filaA = 0x0101010101010101;
        int fileIndex = piece.Square.File;
        return filaA << fileIndex | filaA << Math.Max(0, fileIndex - 1) | filaA << Math.Min(7, fileIndex + 1);
    }
    
    int DistanceBetweenKings()
    {
        Square whiteKing = board.GetKingSquare(true);
        Square blackKing = board.GetKingSquare(false);

        return Math.Abs(blackKing.Rank - whiteKing.Rank) + Math.Abs(blackKing.File - whiteKing.File);
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
    {
        return BitsSet(board.GetPieceBitboard(pieceType, true) | board.GetPieceBitboard(pieceType, false));
    }
}