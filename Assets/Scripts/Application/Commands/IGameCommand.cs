using Peribind.Domain.Board;

namespace Peribind.Application.Commands
{
    public interface IGameCommand
    {
        void Apply(BoardState board);
        void Undo(BoardState board);
    }
}
