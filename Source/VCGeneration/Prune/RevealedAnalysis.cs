#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Boogie;

namespace VCGeneration.Prune;

record RevealedState(HideRevealCmd.Modes Mode, IImmutableSet<Function> Offset) {
  public bool IsRevealed(Function function) {
    return (Mode == HideRevealCmd.Modes.Hide) == Offset.Contains(function);
  }

  public static readonly RevealedState AllRevealed = new(HideRevealCmd.Modes.Reveal, ImmutableHashSet<Function>.Empty);
  public static readonly RevealedState AllHidden = new(HideRevealCmd.Modes.Hide, ImmutableHashSet<Function>.Empty);
}

class RevealedAnalysis : DataflowAnalysis<Cmd, ImmutableStack<RevealedState>> {
  
  public RevealedAnalysis(IReadOnlyList<Cmd> roots, 
    Func<Cmd, IEnumerable<Cmd>> getNext, 
    Func<Cmd, IEnumerable<Cmd>> getPrevious) : base(roots, getNext, getPrevious)
  {
  }

  protected override ImmutableStack<RevealedState> Empty => ImmutableStack<RevealedState>.Empty.Push(
    RevealedState.AllRevealed);

  protected override ImmutableStack<RevealedState> Merge(ImmutableStack<RevealedState> first, ImmutableStack<RevealedState> second) {
    var firstTop = first.Peek();
    var secondTop = second.Peek();
    var mergedTop = MergeStates(firstTop, secondTop);
    return ImmutableStack.Create(mergedTop);
  }

  protected override bool StateEquals(ImmutableStack<RevealedState> first, ImmutableStack<RevealedState> second) {
    return first.Peek().Equals(second.Peek());
  }

  /// <summary>
  /// Takes the union of what is revealed.
  /// </summary>
  public static RevealedState MergeStates(RevealedState first, RevealedState second) {
    if (first.Mode == HideRevealCmd.Modes.Reveal && second.Mode == HideRevealCmd.Modes.Reveal) {
      var intersect = first.Offset.Intersect(second.Offset);
      if (intersect.Count == first.Offset.Count) {
        return first;
      }
      return new RevealedState(HideRevealCmd.Modes.Reveal, intersect);
    }

    if (first.Mode == HideRevealCmd.Modes.Reveal) {
      return first;
    }
    
    if (second.Mode == HideRevealCmd.Modes.Reveal) {
      return second;
    }

    var union = first.Offset.Union(second.Offset);
    if (union.Count == first.Offset.Count) {
      return first;
    }
    return new RevealedState(HideRevealCmd.Modes.Hide, union);
  }

  static RevealedState GetUpdatedState(HideRevealCmd hideRevealCmd, RevealedState state) {
    if (hideRevealCmd.Function == null) {
      return new RevealedState(hideRevealCmd.Mode, ImmutableHashSet<Function>.Empty);
    }

    if (hideRevealCmd.Mode == state.Mode) {
      return state;
    }

    return state with { Offset = state.Offset.Add(hideRevealCmd.Function) };
  }
  
  protected override ImmutableStack<RevealedState> Update(Cmd node, ImmutableStack<RevealedState> state) {
    if (node is ChangeScope changeScope) {
      return changeScope.Mode == ChangeScope.Modes.Push 
        ? state.Push(state.Peek()) 
        : state.Pop();
    }

    if (node is HideRevealCmd hideRevealCmd) {
      var latestState = state.Peek();
      var updatedState = GetUpdatedState(hideRevealCmd, latestState);
      return updatedState.Equals(latestState) ? state : state.Pop().Push(updatedState);
    }

    return state;
  }
}