using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;
using ChessChallenge.Application.APIHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public class EvilBotMine : IChessBot
{
    private Timer timer;
    
    private readonly int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };
    private readonly int[] k_mvvValues = { 0, 10, 20, 30, 40, 50, 0 };
    private readonly int[] k_lvaValues = { 0, 5, 4, 3, 2, 1, 0 };
    
    private int GetMovePriority(Move move, Board board)
    {
        int priority = 0;
        Transposition tp = tpTable[board.ZobristKey & tpMask];
        if(tp.move == move && tp.zobristHash == board.ZobristKey) priority += 1000;
        if (move.IsCapture) priority = k_mvvValues[(int)move.CapturePieceType] + k_lvaValues[(int)move.MovePieceType];
        return priority;
    }
    
    private Transposition[] tpTable;
    private ulong tpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
    
    // Big table packed with data from premade piece square tables
    private readonly ulong[,] PackedEvaluationTables = {
        { 58233348458073600, 61037146059233280, 63851895826342400, 66655671952007680 },
        { 63862891026503730, 66665589183147058, 69480338950193202, 226499563094066 },
        { 63862895153701386, 69480338782421002, 5867015520979476,  8670770172137246 },
        { 63862916628537861, 69480338782749957, 8681765288087306,  11485519939245081 },
        { 63872833708024320, 69491333898698752, 8692760404692736,  11496515055522836 },
        { 63884885386256901, 69502350490469883, 5889005753862902,  8703755520970496 },
        { 63636395758376965, 63635334969551882, 21474836490,       1516 },
        { 58006849062751744, 63647386663573504, 63625396431020544, 63614422789579264 }
    };

    private int maxDepth = 8;
    private int maxTime = 3000;
    
    public EvilBotMine()
    {
        tpTable = new Transposition[tpMask + 1];
    }
    
    public Move Think(Board board, Timer _timer)
    {
        timer = _timer;

        Transposition bestMove = tpTable[board.ZobristKey & tpMask];

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            Search(board, depth, int.MinValue, int.MaxValue);
            bestMove = tpTable[board.ZobristKey & tpMask];

            if (!ShouldExecuteNextDepth(maxTime)) break;
        }
        
        Console.WriteLine("Best move is " + bestMove.move + " with a score of " + bestMove.evaluation + ". Depth Reached: " + bestMove.depth);
        
        return bestMove.move;
    }

    int Search (Board board, int depth, int alpha, int beta)
    {
        if (depth <= 0) return FinalSearch(board, alpha, beta);
        int bestEval = int.MinValue;
        int startingAlpha = alpha;

        ref Transposition transposition = ref tpTable[board.ZobristKey & tpMask];
        if (transposition.zobristHash == board.ZobristKey && transposition.flag != INVALID && transposition.depth >= depth)
        {
            if(transposition.flag == EXACT) return transposition.evaluation;
            if(transposition.flag == LOWERBOUND) alpha = Math.Max(alpha, transposition.evaluation);
            else if(transposition.flag == UPPERBOUND) beta = Math.Min(beta, transposition.evaluation);
            if(alpha >= beta) return transposition.evaluation;
        }

        Move[] moves = board.GetLegalMoves();
        if (board.IsDraw()) return -10;
        if (board.IsInCheckmate()) return int.MinValue + board.PlyCount;

        OrderMoves(ref moves, board);
        
        Move bestMove = moves[0];

        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int eval = -Search(board, depth - 1, -beta, -alpha);
            board.UndoMove(move);

            if (bestEval < eval)
            {
                bestMove = move;
                bestEval = eval;
            }
            
            alpha = Math.Max(eval, alpha);
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

    int FinalSearch(Board board, int alpha, int beta)
    {
        Move[] moves;
        if (board.IsInCheck()) moves = board.GetLegalMoves();
        else
        {
            moves = board.GetLegalMoves(true);
            if (board.IsDraw()) return -1000 + board.PlyCount;
            if (board.IsInCheckmate()) return int.MinValue + board.PlyCount;
            if (moves.Length == 0) return Evaluate(board);
        }

        Transposition transposition = tpTable[board.ZobristKey & tpMask];
        if (transposition.zobristHash == board.ZobristKey && transposition.flag != INVALID && transposition.depth >= 0)
        {
            if(transposition.flag == EXACT) return transposition.evaluation;
            if(transposition.flag == LOWERBOUND) alpha = Math.Max(alpha, transposition.evaluation);
            else if(transposition.flag == UPPERBOUND) beta = Math.Min(beta, transposition.evaluation);
            if(alpha >= beta) return transposition.evaluation;
        }

        alpha = Math.Max(Evaluate(board), alpha);
        if (alpha >= beta) return beta;

        OrderMoves(ref moves, board);
        
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int eval = -FinalSearch(board, -beta, -alpha);
            board.UndoMove(move);

            alpha = Math.Max(eval, alpha);
            if (alpha > beta) break;
        }

        return alpha;
    }

    int Evaluate(Board board)
    {
        int materialCount = 0;
        int PSTscores = 0;
        for (int i = 0; ++i < 7;)
        {
            PieceList white_pl = board.GetPieceList((PieceType)i, true);
            PieceList black_pl = board.GetPieceList((PieceType)i, false);
            materialCount += (white_pl.Count - black_pl.Count) * pieceValues[i];
            for(int j = 0; j < 9; j++)
            {
                if(j < white_pl.Count) PSTscores += GetSquareBonus((PieceType)i, true, white_pl[j].Square.File, white_pl[j].Square.Rank);
                if(j < black_pl.Count) PSTscores -= GetSquareBonus((PieceType)i, false, black_pl[j].Square.File, black_pl[j].Square.Rank);
            }
        }

        return (board.IsRepeatedPosition() ? -500 : 0) + ((materialCount + PSTscores) * (board.IsWhiteToMove ? 1 : -1));
    }
    
    
    private void OrderMoves(ref Move[] moves, Board board)
    {
        List<Tuple<Move, int>> orderedMoves = new();
        foreach(Move m in moves) orderedMoves.Add(new Tuple<Move, int>(m, GetMovePriority(m, board)));
        orderedMoves.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        for(int i = 0; i < moves.Length; i++) moves[i] = orderedMoves[i].Item1;
    }

    private bool ShouldExecuteNextDepth(int maxThinkTime)
    {
        int currentThinkTime = timer.MillisecondsElapsedThisTurn;
        return ((maxThinkTime - currentThinkTime) > currentThinkTime * 3);
    }
    
    private int GetSquareBonus(PieceType type, bool isWhite, int file, int rank)
    {
        // Because arrays are only 4 squares wide, mirror across files
        if (file > 3)
            file = 7 - file;

        // Mirror vertically for white pieces, since piece arrays are flipped vertically
        if (isWhite)
            rank = 7 - rank;

        // First, shift the data so that the correct byte is sitting in the least significant position
        // Then, mask it out
        sbyte unpackedData = (sbyte)((PackedEvaluationTables[rank, file] >> 8 * ((int)type - 1)) & 0xFF);

        // Merge the sign back into the original unpacked data
        // by first bitwise-ANDing it in with a sign mask, and then ORing it back into the unpacked data
        unpackedData = (sbyte)((byte)unpackedData | (0b10000000 & unpackedData));

        // Invert eval scores for black pieces
        return isWhite ? unpackedData : -unpackedData;
    }

    private const byte INVALID = 0, EXACT = 1, LOWERBOUND = 2, UPPERBOUND = 3;

    //14 bytes per entry, likely will align to 16 bytes due to padding (if it aligns to 32, recalculate max TP table size)
    public struct Transposition
    {
        public ulong zobristHash;
        public Move move;
        public int evaluation;
        public int depth;
        public byte flag;
    };
}