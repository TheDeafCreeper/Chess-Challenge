using System;
using System.Collections.Generic;
using ChessChallenge.API;

public class MyBotOld : IChessBot
{
    struct TTEntry {
        public ulong key;
        public Move move;
        public int depth, score, bound;
        public TTEntry(ulong _key, Move _move, int _depth, int _score, int _bound) {
            key = _key; move = _move; depth = _depth; score = _score; bound = _bound;
        }
    }

    struct PVInfo//#DEBUG
    {//#DEBUG
        public Move move = Move.NullMove;//#DEBUG
        public Boolean isCheck;//#DEBUG
        public Boolean isCheckmate;//#DEBUG
        public Boolean isWhitesMove;//#DEBUG

        public PVInfo(Move _move, Boolean _isCheck, Boolean _isCheckmate, Boolean _isWhitesMove)//#DEBUG
        { move = _move; isCheck = _isCheck; isCheckmate = _isCheckmate; isWhitesMove = _isWhitesMove; }//#DEBUG
    }//#DEBUG

    const int entries = (1 << 20), CheckmateValue = -100000;
    TTEntry[] tt = new TTEntry[entries];
    
    Board board;
    Timer timer;
    
    int bestMoveScore, bestMoveDepth, nodesChecked;//#DEBUG

    Move bestRootMove = Move.NullMove;
    
    public Move Think(Board _board, Timer _timer)
    {
        board = _board;
        timer = _timer;

        bestRootMove = Move.NullMove;//#DEBUG
        bestMoveScore = CheckmateValue;//#DEBUG
        bestMoveDepth = -1;//#DEBUG

        int prevIterTime = 0;//#DEBUG

        Span <PVInfo> pvInfo = stackalloc PVInfo[64];//#DEBUG

        int i = 0;
        while (i++ < 50)
        {
            //pvInfo.Clear();
            Search(i, CheckmateValue, -CheckmateValue, 0, 0,
                pvInfo//#DEBUG
                );
            
            //Console.WriteLine($"Depth {i} took {timer.MillisecondsElapsedThisTurn - prevIterTime}ms");//#DEBUG
            prevIterTime = timer.MillisecondsElapsedThisTurn;//#DEBUG
            
            if(shouldStopSearch()) break;
        }
        
        // Console.WriteLine($"Board Fen: {board.GetFenString()}");//#DEBUG
        // Console.WriteLine($"Best move was {bestRootMove} with a score of {(float)bestMoveScore / 100} at depth {bestMoveDepth}.");//#DEBUG
        // Console.WriteLine($"There were {nodesChecked} nodes checked in {(float)timer.MillisecondsElapsedThisTurn / 1000} Seconds.");//#DEBUG

        String pvString = "PV:";//#DEBUG

        String[] pieceCharacters = { "♟︎", "♞", "♝", "♜", "♛", "♚", "♙", "♘", "♗", "♖", "♕", "♔" };//#DEBUG
        int pvsCounted = 0;//#DEBUG
        foreach (PVInfo PVINFO in pvInfo)//#DEBUG
        {//#DEBUG
            //if (++pvsCounted > bestMoveDepth) break;//#DEBUG
            Move move = PVINFO.move;//#DEBUG
            if (move.IsNull) break;//#DEBUG
            String moveString = "";//#DEBUG

            if (move.IsCastles)//#DEBUG
            {//#DEBUG
                if (move.TargetSquare.File == 2) moveString = "O-O-O";//#DEBUG
                else if (move.TargetSquare.File == 6) moveString = "O-O";//#DEBUG
            }//#DEBUG
            else//#DEBUG
            {//#DEBUG
                moveString += pieceCharacters[((int)move.MovePieceType - 1) + (PVINFO.isWhitesMove ? 6 : 0)];//#DEBUG
                moveString += $"{move.StartSquare.Name}";//#DEBUG
                if (move.IsCapture) moveString += "x";//#DEBUG
                moveString += move.TargetSquare.Name;//#DEBUG
                if (PVINFO.isCheckmate) moveString += "#";//#DEBUG
                else if (PVINFO.isCheck) moveString += "+";//#DEBUG
            }//#DEBUG
            
            if (PVINFO.isCheckmate) moveString += PVINFO.isWhitesMove ? " 0-1" : " 1-0";//#DEBUG
            pvString += $" {moveString}";//#DEBUG
            
            if (PVINFO.isCheckmate) break;//#DEBUG
        }//#DEBUG
        //Console.WriteLine(pvString);//#DEBUG
        //Console.WriteLine("=============================================");//#DEBUG
        
        TTEntry entry = tt[board.ZobristKey % entries];
        if (bestRootMove.IsNull && entry.bound != 0) bestRootMove = entry.move;
        return bestRootMove.IsNull ? board.GetLegalMoves()[0] : bestRootMove;
    }

    int Search(int depth, int alpha, int beta, int ply, int extensions,
        Span<PVInfo> pvList//#DEBUG
        )
    {
        nodesChecked++;//#DEBUG
        ulong key = board.ZobristKey;
        bool qSearch = depth <= 0, notRoot = ply > 0;
        
        if(notRoot && board.IsRepeatedPosition()) return 0;

        TTEntry entry = tt[key % entries];
        
        // TT cutoffs
        if(notRoot && entry.key == key && entry.depth >= depth && (
               entry.bound == 3 // exact score
               || entry.bound == 2 && entry.score >= beta // lower bound, fail high
               || entry.bound == 1 && entry.score <= alpha // upper bound, fail low
           ))
        {//#DEBUG
            pvList[0] = new PVInfo(entry.move, board.IsInCheck(), board.IsInCheckmate(), board.IsWhiteToMove);//#DEBUG
            return entry.score;
        }//#DEBUG

        int bestEval = CheckmateValue, eval = Evaluation(), startingAlpha = alpha;

        if (qSearch)
        {
            bestEval = eval;
            if (bestEval >= beta) return eval;
            alpha = Math.Max(alpha, bestEval);
        }

        Span<PVInfo> childPV = stackalloc PVInfo[64];//#DEBUG
        
        Move[] moves = board.GetLegalMoves(qSearch);
        OrderMoves(ref moves);
        
        Move bestMove = Move.NullMove;

        int movesChecked = 0;
        
        foreach (Move move in moves)
        {
            childPV.Clear();//#DEBUG
            if(shouldStopSearch()) return -CheckmateValue;
            
            board.MakeMove(move);
            int score, extension = 0;
            if ((move.IsPromotion || board.IsInCheck() || moves.Length == 1) && !qSearch && extensions < 16) extension++;
            
            extensions += extension;
            
            ply++;
            if (movesChecked++ == 0)
                score = -Search(depth - 1 + extension, -beta, -alpha, ply, extensions
                    , childPV //#DEBUG
                );
            else if (movesChecked > 5 && extension == 0) score = -Search(depth / 2, -alpha - 1, -alpha, ply, extensions
                , childPV//#DEBUG
            );
            else score = -Search(depth - 1 + extension, -alpha - 1, -alpha, ply, extensions
                , childPV//#DEBUG
            );
            
            if (alpha < score && score < beta) score = -Search(depth - 1 + extension, -beta, -alpha, ply, extensions
                , childPV //#DEBUG
            );

            if (score > bestEval)//#DEBUG
            {//#DEBUG
                pvList[0] = new PVInfo(move, board.IsInCheck(), board.IsInCheckmate(), board.IsWhiteToMove);//#DEBUG
                for (int i = 0; i < 64; i++)//#DEBUG
                {//#DEBUG
                    if (childPV[i].move == Move.NullMove) break;//#DEBUG
                    pvList[i + 1] = childPV[i];//#DEBUG
                }//#DEBUG
            }//#DEBUG
            
            board.UndoMove(move);
            
            if (score > bestEval)
            {
                bestEval = score;
                bestMove = move;
                if (!notRoot) bestRootMove = move;
                if (!notRoot) bestMoveScore = score;//#DEBUG
                if (!notRoot) bestMoveDepth = depth;//#DEBUG
            }
            if (score >= beta) return beta;
            
            alpha = Math.Max(alpha, score);
        }

        if(!qSearch && moves.Length == 0) return board.IsInCheck() ? CheckmateValue + ply : 0;
        int bound = bestEval >= beta ? 2 : bestEval > startingAlpha ? 3 : 1;
        tt[key % entries] = new TTEntry(key, bestMove, depth, bestEval, bound);

        return alpha;
    }

    Boolean shouldStopSearch() => timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / 30;
    
    void OrderMoves(ref Move[] moves)
    {
        List<(Move, int)> orderedMoves = new();
        foreach(Move move in moves) orderedMoves.Add(new (move, GetMovePriority(move)));
        orderedMoves.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        for(int i = 0; i < moves.Length; i++) moves[i] = orderedMoves[i].Item1;
    }
    
    int GetMovePriority(Move move)
    {
        TTEntry entry = tt[board.ZobristKey % entries];
        return entry.move == move && entry.key == board.ZobristKey ? 1000 : move.IsCapture ? 100 * (int)move.CapturePieceType - (int)move.MovePieceType : 0;
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