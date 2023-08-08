#define LOGGING

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Chess_Challenge.Stockfish;
using ChessChallenge.API;

namespace Chess_Challenge.My_Bot;

public class MyBotMCTS : IChessBot {

    struct TreeNode {
        public float Visits;        
        public float Value;
        public TreeNode[] Children;
        public Move[] Moves;
        public float[] Policy;
        public int depth;
    }

    private int iterationsCompleted;//#DEBUG
    
    private TreeNode root;
    Board board;
    
    public Move Think(Board startBoard, Timer timer)
    {
        board = startBoard;
        
        Boolean foundNewRoot = false;
        if (root.Children != null && root.Children.Length > 0)
        {

            for (int i = 0; i < root.Moves.Length; i++)
                if (root.Moves[i] == board.GameMoveHistory.Last())
                {
                    root = root.Children[i];
                    foundNewRoot = true;
                    break;
                }
        }
            
        if (!foundNewRoot) root = new TreeNode();

        int timeRemaining = timer.MillisecondsRemaining;
        int maxTime = Math.Min(15000, timeRemaining / 30);
        int checkTimeExtension = Math.Min(timeRemaining - 100, board.IsInCheck() ? 750 : 0);
        if (timeRemaining > timer.OpponentMillisecondsRemaining && timeRemaining > 5000)
            maxTime +=(timeRemaining - timer.OpponentMillisecondsRemaining) / 4;
        
        while (timer.MillisecondsElapsedThisTurn < maxTime + checkTimeExtension && root.Visits < 900000)
        //while (root.Visits < 900000)
            iteration(ref root);

        Move bestMove;
        TreeNode bestNode = new();//#DEBUG
        float bestAvg = -1f;
        float secondBestAverage = -1f;//#DEBUG
        if (root.Moves == null || root.Moves.Length == 0) bestMove = board.GetLegalMoves()[0];
        else
        {
            bestAvg = -1f;
            bestMove = root.Moves[0];
            bestNode = root.Children[0];//#DEBUG
            for (int i = 0; i < root.Moves.Length; i++)
            {
                TreeNode child = root.Children[i];
                float avg = -child.Value / child.Visits;
                if (avg > bestAvg)
                {
                    bestAvg = avg;
                    bestMove = root.Moves[i];
                    bestNode = child;//#DEBUG
                }
                if (avg < bestAvg && avg > secondBestAverage) secondBestAverage = avg;//#DEBUG
            }
        }
            
        Console.WriteLine("Completed " + iterationsCompleted + " in " + timer.MillisecondsElapsedThisTurn + "ms. Move Eval: " + bestAvg);//#DEBUG
        Console.WriteLine("Second Best Eval Was: " + secondBestAverage);//#DEBUG
            
        TreeNode continuationNode = bestNode;//#DEBUG
        int contCount = 0;//#DEBUG

        String continuationString = $"[{bestMove}]";//#DEBUG
        Boolean opponentMove = true;//#DEBUG
            
        while (continuationNode.Children != null && continuationNode.Children.Length > 0)//#DEBUG
        {//#DEBUG
            float bestAvgCont = -1f;//#DEBUG
            Move bestMoveCont = continuationNode.Moves[0];//#DEBUG
            TreeNode bestNodeCont = continuationNode.Children[0];//#DEBUG
            for (int i = 0; i < continuationNode.Moves.Length; i++)//#DEBUG
            {//#DEBUG
                TreeNode child = continuationNode.Children[i];//#DEBUG
                float avg = -child.Value / child.Visits;//#DEBUG
                if (avg > bestAvgCont)//#DEBUG
                {//#DEBUG
                    bestAvgCont = avg;//#DEBUG
                    bestMoveCont = continuationNode.Moves[i];//#DEBUG
                    bestNodeCont = child;//#DEBUG
                }//#DEBUG
            }//#DEBUG

            contCount++;//#DEBUG
            continuationString += $", {bestMoveCont} ({bestAvgCont})";//#DEBUG
            continuationNode = bestNodeCont;//#DEBUG
            opponentMove = !opponentMove;//#DEBUG
        }//#DEBUG
            
        Console.WriteLine($"Continuation is {continuationString}");//#DEBUG
        Console.WriteLine("===============================");//#DEBUG
            
        iterationsCompleted = 0;//#DEBUG
            
        return bestMove;
    }
    
    // Iteration() handles all the core steps of the MCTS algorithm
    float iteration(ref TreeNode node)
    {
        iterationsCompleted++;//#DEBUG
        // If we have reached a leaf node, we enter the EXPANSION step base case
        if (node.Visits == 0){
            node.Visits = 1;
            node.Value = Evaluation();//qSearch(float.MinValue, float.MaxValue);
            
            return node.Value;
        }

        // Most leaf nodes will not be revisited, so only call expensive movegen on revisit
        if (node.Visits == 1){
            node.Moves = board.GetLegalMoves();
            node.Children = new TreeNode[node.Moves.Length];
            node.Policy = new float[node.Moves.Length];
            
            for (int i = 0; i < node.Moves.Length; i++)
            {
                node.Children[i].depth = node.depth + 1;
                node.Policy[i] = GetMovePriority(node.Moves[i]);
            }
        }
        
        if (node.Moves.Length == 0)
            return node.Value / node.Visits;
        
        float part = 1.41f * MathF.Log(node.Visits); 
        float bestUCT = -1;
        
        int bestChildIdx = 0; 

        for (int i = 0; i < node.Moves.Length; i++){
            TreeNode child = node.Children[i];
            float policy = node.Policy[i];
            
            float uct;
            if (child.Visits == 0)
                uct = 100f + policy;
            else uct = (-child.Value / child.Visits) + MathF.Sqrt(part / child.Visits) + (policy / child.Visits);
            
            if (uct > bestUCT){
                bestUCT = uct;
                bestChildIdx = i;
            }
        }
        
        Move exploreMove = node.Moves[bestChildIdx];
        board.MakeMove(exploreMove);
        float eval = -iteration(ref node.Children[bestChildIdx]);
        node.Value += eval;
        node.Visits++;
        board.UndoMove(exploreMove);

        return eval;
    }
    
    private float GetMovePriority(Move move)
    {
        float priority = 0;
        if (move.IsCapture) priority = 10 * (int)move.CapturePieceType - (int)move.MovePieceType; // 10, 20, 30, 40, 50, 60 - 1 2 3 4 5 6
        if (move.IsPromotion) priority += 10;
        if (move.IsCastles) priority += 20;
        
        // Max = 59;
        
        return priority / 70;
    }
    
    int[] pieceVal = {0, 100, 310, 330, 500, 1000, 10000 };
    int[] piecePhase = {0, 0, 1, 1, 2, 4, 0};
    ulong[] psts = {657614902731556116, 420894446315227099, 384592972471695068, 312245244820264086, 364876803783607569, 366006824779723922, 366006826859316500, 786039115310605588, 421220596516513823, 366011295806342421, 366006826859316436, 366006896669578452, 162218943720801556, 440575073001255824, 657087419459913430, 402634039558223453, 347425219986941203, 365698755348489557, 311382605788951956, 147850316371514514, 329107007234708689, 402598430990222677, 402611905376114006, 329415149680141460, 257053881053295759, 291134268204721362, 492947507967247313, 367159395376767958, 384021229732455700, 384307098409076181, 402035762391246293, 328847661003244824, 365712019230110867, 366002427738801364, 384307168185238804, 347996828560606484, 329692156834174227, 365439338182165780, 386018218798040211, 456959123538409047, 347157285952386452, 365711880701965780, 365997890021704981, 221896035722130452, 384289231362147538, 384307167128540502, 366006826859320596, 366006826876093716, 366002360093332756, 366006824694793492, 347992428333053139, 457508666683233428, 329723156783776785, 329401687190893908, 366002356855326100, 366288301819245844, 329978030930875600, 420621693221156179, 422042614449657239, 384602117564867863, 419505151144195476, 366274972473194070, 329406075454444949, 275354286769374224, 366855645423297932, 329991151972070674, 311105941360174354, 256772197720318995, 365993560693875923, 258219435335676691, 383730812414424149, 384601907111998612, 401758895947998613, 420612834953622999, 402607438610388375, 329978099633296596, 67159620133902};
    
    public int getPstVal(int psq) {
        return (int)(((psts[psq / 10] >> (6 * (psq % 10))) & 63) - 20) * 8;
    }
    
    public float Evaluation() {
        if (board.IsRepeatedPosition() || board.IsInsufficientMaterial() || board.IsFiftyMoveDraw()) return 0f;
        if (board.IsInCheckmate()) return -1f;
        
        int mg = 0, eg = 0, phase = 0;

        foreach(bool stm in new[] {true, false}) {
            for(var p = PieceType.Pawn; p <= PieceType.King; p++) {
                int piece = (int)p, ind;
                ulong mask = board.GetPieceBitboard(p, stm);
                while(mask != 0) {
                    phase += piecePhase[piece];
                    ind = 128 * (piece - 1) + BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ (stm ? 56 : 0);
                    mg += getPstVal(ind) + pieceVal[piece];
                    eg += getPstVal(ind + 64) + pieceVal[piece];
                }
            }

            mg = -mg;
            eg = -eg;
        }

        float score = (mg * phase + eg * (24 - phase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
        return 0.9f * MathF.Tanh(score / 250);
    }

    float qSearch(float alpha, float beta)
    {
        float patScore = Evaluation();
        if (patScore > beta) return beta;
        alpha = Math.Max(patScore, alpha);

        Move[] legalMoves = board.GetLegalMoves(true);
        foreach (Move move in legalMoves)
        {
            board.MakeMove(move);
            float eval = -qSearch(-beta, -alpha);
            board.UndoMove(move);

            if (eval >= beta) return beta;
            alpha = Math.Max(eval, alpha);
        }

        return alpha;
    }
}