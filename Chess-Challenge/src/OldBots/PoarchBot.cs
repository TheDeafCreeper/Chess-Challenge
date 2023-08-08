using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;


/// <summary>
/// Main bot class that does the thinking.
/// </summary>
public class PoarchBot : IChessBot
{
	public Move Think(Board board, Timer timer)
    {
        Move[] moves = board.GetLegalMoves();
        var valueList = new int[moves.Length];
        int moveValue = -999999999;

        //Determine if MyBot is playing white
        bool isWhite = board.IsWhiteToMove;

        //Check the number of pawns you currently have in play
        int numPawns = 8 - board.GetPieceList(PieceType.Pawn, isWhite).Count;

        // Piece values: null, pawn, knight, bishop, rook, queen, king
        int[] pieceValues =
        {
            0,
            150 + numPawns*25 + board.PlyCount,
            300,
            400,
            500 + board.PlyCount,
            10000,
            99999
        };

        //Loop through all legal moves
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];

            //Evaluate the current move and update the current move value, creating a list of values as we go
            int currentValue = EvaluateMove(board, move, isWhite, pieceValues);
            valueList[i] = currentValue;

            if (currentValue > moveValue)
                moveValue = currentValue;
        }

        //call Choose a Move to... Choose a Move
        int moveToUse = ChooseAMove(moveValue, valueList);
        Move bestMove = moves[moveToUse];
        return bestMove;
    }

    static int EvaluateMove(Board board, Move move, bool isWhite, int[] pieceValues)
    {
        //Sets current value to the value of the piece to start out (or 0 for no piece capture)
        int currentValue = pieceValues[(int)move.CapturePieceType];

        //If the piece that's moving is the king, decentivise moving forward, but lay off this as turns pass, can even turn into a benefit for moving the king in late game
        if (move.MovePieceType == PieceType.King && !isWhite && move.StartSquare.Rank > move.TargetSquare.Rank)
        {
            currentValue -= 100 - board.PlyCount;
        }
        else if (move.MovePieceType == PieceType.King && isWhite && move.StartSquare.Rank < move.TargetSquare.Rank)
        {
            currentValue -= 100 - board.PlyCount;
        }

        //If the move is to promote a pawn, promote to queen unless that's a bad move for other reasons
        currentValue += move.IsPromotion ? pieceValues[(int)move.PromotionPieceType]/100 : 0;
         
        //If captured piece is passed pawn, incentivize taking it
        currentValue += IsPassedPawn(board, move.TargetSquare, move.CapturePieceType, !isWhite) ? 50 : 0;

        //If move piece is passed pawn, move it forward
        currentValue += IsPassedPawn(board, move.StartSquare, move.MovePieceType, isWhite) ? 100 + board.PlyCount : 0;

        int numPassedBefore = NumPassedPawn(board, isWhite);
        int numLegalMovesBefore = board.GetLegalMoves().Length;
        int numLegalAttacksBefore = board.GetLegalMoves(true).Length;

        board.MakeMove(move);

        int numPassedAfter = NumPassedPawn(board, isWhite);
        int numLegalMovesAfter = board.GetLegalMoves().Length;
        int numLegalAttacksAfter = board.GetLegalMoves(true).Length;

        //Incentivze creating passed pawns and opening legal moves and captures
        currentValue += numPassedAfter > numPassedBefore ? 50 + board.PlyCount : 0;
        currentValue += numLegalMovesAfter > numLegalMovesBefore ? 200 + board.PlyCount : 0;
        currentValue += numLegalAttacksAfter > numLegalAttacksBefore ? 50 : 0;

        //If it's checkmate, we basically just want to do that
        currentValue += board.IsInCheckmate() ? 999999 : 0;

        //If it puts them in check, it gets a bonus. But, and I actually originally did this on accident, but it really worked;
        //If it puts them in check and also captures, it's slightly less of a bonus, because those are already good moves
        currentValue += board.IsInCheck() ? (move.IsCapture ? 400 : 200 + board.PlyCount ) : 0;

        //And, if it would cause a draw, we disincentivise that, though there's often not a lot you can do about it
        currentValue -= board.IsDraw() ? 99999 : 0;
        board.UndoMove(move);

        //This is probably my favorite part of my bot, the DangerValue function. I'll explain in detail when we get there.
        currentValue -= DangerValue(board, move, pieceValues);

        return currentValue;
    }

    static int ChooseAMove(int moveValue, int[] valueList)
    {
        Random rng = new();
        List<int> indexList = new();

        //Loop through the list of values we made earlier, finding ones that match the moveValue we evaluated and adding them to a list
        for (int i = 0; i < valueList.Length; i++)
        {
            if (valueList[i] == moveValue)
                indexList.Add(i);
        }

        //index is a random index from the list we just created
        int index = rng.Next(indexList.Count);

        //Return the index of the move randomly selected from all the moves that tied for highest value
        int move = indexList[index];
        return move;
    }

    static int DangerValue(Board board, Move move, int[] pieceValues)
    {
        //Calculate Danger before, make move and calculate Danger after the move
        board.MakeMove(Move.NullMove);
        int DangerBefore = CountDanger(board, pieceValues);
        board.UndoMove(Move.NullMove);
        board.MakeMove(move);
        int DangerAfter = CountDanger(board, pieceValues);
        board.UndoMove(move);

        //Subtract the DangerAfter from the Danger before, giving a value that decentivizes putting major pieces in jeopardy
        //But also that incentivizes protecting those same pieces
        int danger = DangerAfter - DangerBefore;

        return danger;
    }

    static int CountDanger(Board board, int[] pieceValues)
    {
        int dangerValue = 0;
        int numAttacks = 0;
        //Since we've already done MakeMove in the larger context, we can just use getlegalmoves to get opponents moves (thanks community!)
        Move[] captureMoves = board.GetLegalMoves(true);

        //Loop through all legal capture moves, get the piece value, update danger value
        for (int i = 0; i < captureMoves.Length; i++)
        {
            int tempValue = pieceValues[(int)captureMoves[i].CapturePieceType];

            if (tempValue > dangerValue)
                dangerValue = tempValue;

            numAttacks++;
        }
        //Danger value calculated the same as before, so that it mostly evens out when subtracting the two, unless something real bad is gonna happen
        dangerValue += numAttacks * 50;

        return dangerValue;
    }

    static bool IsPassedPawn(Board board, Square square, PieceType piece, bool isWhite)
    {
        int numPawnsInRank = 0;
        PieceList pieces = board.GetPieceList(PieceType.Pawn, isWhite);

        if (piece == PieceType.Pawn)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].Square.Rank == square.Rank)
                    numPawnsInRank++;
            }

            if (numPawnsInRank == 1)
            {
                return true;
            }
        }
        return false;
    }

    static int NumPassedPawn(Board board, bool isWhite)
    {
        int numPassedPawns = 0;
        PieceList pieces = board.GetPieceList(PieceType.Pawn, isWhite);

        for (int i = 0; i < pieces.Count; i++)
        {
            if (IsPassedPawn(board, pieces.GetPiece(i).Square, PieceType.Pawn, isWhite))
                numPassedPawns++;
        }
        return numPassedPawns;
    }
}