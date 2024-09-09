using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Microsoft.Boogie.TypeErasure;

[ContractClass(typeof(MapTypeAbstractionBuilderContracts))]
internal abstract class MapTypeAbstractionBuilder
{
  protected readonly TypeAxiomBuilder /*!*/
    AxBuilder;

  protected readonly VCExpressionGenerator /*!*/
    Gen;

  [ContractInvariantMethod]
  void ObjectInvariant()
  {
    Contract.Invariant(AxBuilder != null);
    Contract.Invariant(Gen != null);
  }


  internal MapTypeAbstractionBuilder(TypeAxiomBuilder axBuilder, VCExpressionGenerator gen)
  {
    Contract.Requires(gen != null);
    Contract.Requires(axBuilder != null);
    this.AxBuilder = axBuilder;
    this.Gen = gen;
    AbstractionVariables = new List<TypeVariable /*!*/>();
    ClassRepresentations = new Dictionary<MapType /*!*/, MapTypeClassRepresentation>();
  }

  // constructor for cloning
  internal MapTypeAbstractionBuilder(TypeAxiomBuilder axBuilder, VCExpressionGenerator gen,
    MapTypeAbstractionBuilder builder)
  {
    Contract.Requires(builder != null);
    Contract.Requires(gen != null);
    Contract.Requires(axBuilder != null);
    this.AxBuilder = axBuilder;
    this.Gen = gen;
    AbstractionVariables =
      new List<TypeVariable /*!*/>(builder.AbstractionVariables);
    ClassRepresentations =
      new Dictionary<MapType /*!*/, MapTypeClassRepresentation>(builder.ClassRepresentations);
  }

  ///////////////////////////////////////////////////////////////////////////
  // Type variables used in the abstractions. We use the same variables in the
  // same order in all abstractions in order to obtain comparable abstractions
  // (equals, hashcode)

  private readonly List<TypeVariable /*!*/> /*!*/
    AbstractionVariables;

  [ContractInvariantMethod]
  void AbstractionVariablesInvariantMethod()
  {
    Contract.Invariant(Cce.NonNullElements(AbstractionVariables));
  }

  private TypeVariable AbstractionVariable(int num)
  {
    Contract.Requires((num >= 0));
    Contract.Ensures(Contract.Result<TypeVariable>() != null);
    while (AbstractionVariables.Count <= num)
    {
      AbstractionVariables.Add(new TypeVariable(Token.NoToken,
        "aVar" + AbstractionVariables.Count));
    }

    return AbstractionVariables[num];
  }

  ///////////////////////////////////////////////////////////////////////////
  // The untyped representation of a class of map types, i.e., of a map type
  // <a0, a1, ...>[A0, A1, ...] R, where the argument types and the result type
  // possibly contain free type variables. For each such class, a separate type
  // constructor and separate select/store functions are introduced.

  protected struct MapTypeClassRepresentation
  {
    public readonly TypeCtorDecl /*!*/
      RepresentingType;

    public readonly Function /*!*/
      Select;

    public readonly Function /*!*/
      Store;

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(RepresentingType != null);
      Contract.Invariant(Select != null);
      Contract.Invariant(Store != null);
    }


    public MapTypeClassRepresentation(TypeCtorDecl representingType, Function select, Function store)
    {
      Contract.Requires(store != null);
      Contract.Requires(select != null);
      Contract.Requires(representingType != null);
      this.RepresentingType = representingType;
      this.Select = select;
      this.Store = store;
    }
  }

  private readonly IDictionary<MapType /*!*/, MapTypeClassRepresentation /*!*/> /*!*/
    ClassRepresentations;

  [ContractInvariantMethod]
  void ClassRepresentationsInvariantMethod()
  {
    Contract.Invariant(ClassRepresentations != null);
  }

  protected MapTypeClassRepresentation GetClassRepresentation(MapType abstractedType)
  {
    Contract.Requires(abstractedType != null);
    if (!ClassRepresentations.TryGetValue(abstractedType, out var res))
    {
      int num = ClassRepresentations.Count;
      TypeCtorDecl /*!*/
        synonym =
          new TypeCtorDecl(Token.NoToken, "MapType" + num, abstractedType.FreeVariables.Count);

      GenSelectStoreFunctions(abstractedType, synonym, out var @select, out var store);

      res = new MapTypeClassRepresentation(synonym, select, store);
      ClassRepresentations.Add(abstractedType, res);
    }

    return res;
  }

  // the actual select and store functions are generated by the
  // concrete subclasses of this class
  protected abstract void GenSelectStoreFunctions(MapType /*!*/ abstractedType, TypeCtorDecl /*!*/ synonymDecl,
    out Function /*!*/ select, out Function /*!*/ store);

  ///////////////////////////////////////////////////////////////////////////

  public Function Select(MapType rawType, out List<Type> instantiations)
  {
    Contract.Requires((rawType != null));
    Contract.Ensures(Contract.ValueAtReturn(out instantiations) != null);
    Contract.Ensures(Contract.Result<Function>() != null);
    return AbstractAndGetRepresentation(rawType, out instantiations).Select;
  }

  public Function Store(MapType rawType, out List<Type> instantiations)
  {
    Contract.Requires((rawType != null));
    Contract.Ensures(Contract.ValueAtReturn(out instantiations) != null);
    Contract.Ensures(Contract.Result<Function>() != null);
    return AbstractAndGetRepresentation(rawType, out instantiations).Store;
  }

  private MapTypeClassRepresentation
    AbstractAndGetRepresentation(MapType rawType, out List<Type> instantiations)
  {
    Contract.Requires((rawType != null));
    Contract.Ensures(Contract.ValueAtReturn(out instantiations) != null);
    instantiations = new List<Type>();
    MapType /*!*/
      abstraction = ThinOutMapType(rawType, instantiations);
    return GetClassRepresentation(abstraction);
  }

  public CtorType AbstractMapType(MapType rawType)
  {
    Contract.Requires(rawType != null);
    Contract.Ensures(Contract.Result<CtorType>() != null);
    List<Type> /*!*/
      instantiations = new List<Type>();
    MapType /*!*/
      abstraction = ThinOutMapType(rawType, instantiations);

    MapTypeClassRepresentation repr = GetClassRepresentation(abstraction);
    Contract.Assume(repr.RepresentingType.Arity == instantiations.Count);
    return new CtorType(Token.NoToken, repr.RepresentingType, instantiations);
  }

  // TODO: cache the result of this operation
  protected MapType ThinOutMapType(MapType rawType, List<Type> instantiations)
  {
    Contract.Requires(instantiations != null);
    Contract.Requires(rawType != null);
    Contract.Ensures(Contract.Result<MapType>() != null);
    List<Type> /*!*/
      newArguments = new List<Type>();
    foreach (Type /*!*/ subtype in rawType.Arguments.ToList())
    {
      Contract.Assert(subtype != null);
      newArguments.Add(ThinOutType(subtype, rawType.TypeParameters,
        instantiations));
    }

    Type /*!*/
      newResult = ThinOutType(rawType.Result, rawType.TypeParameters,
        instantiations);
    return new MapType(Token.NoToken, rawType.TypeParameters, newArguments, newResult);
  }

  // the instantiations of inserted type variables, the order corresponds to the order in which "AbstractionVariable(int)" delivers variables
  private Type /*!*/ ThinOutType(Type rawType, List<TypeVariable> boundTypeParams, List<Type> instantiations)
  {
    Contract.Requires(instantiations != null);
    Contract.Requires(boundTypeParams != null);
    Contract.Requires(rawType != null);
    Contract.Ensures(Contract.Result<Type>() != null);

    if (rawType.FreeVariables.All(var => !boundTypeParams.Contains(var)))
    {
      // Bingo!
      // if the type does not contain any bound variables, we can simply
      // replace it with a type variable
      TypeVariable /*!*/
        abstractionVar = AbstractionVariable(instantiations.Count);
      Contract.Assume(!boundTypeParams.Contains(abstractionVar));
      instantiations.Add(rawType);
      return abstractionVar;
    }

    if (rawType.IsVariable)
    {
      //
      // then the variable has to be bound, we cannot do anything
      TypeVariable /*!*/
        rawVar = rawType.AsVariable;
      Contract.Assume(boundTypeParams.Contains(rawVar));
      return rawVar;
      //
    }
    else if (rawType.IsMap)
    {
      //
      // recursively abstract this map type and continue abstracting
      CtorType /*!*/
        abstraction = AbstractMapType(rawType.AsMap);
      return ThinOutType(abstraction, boundTypeParams, instantiations);
      //
    }
    else if (rawType.IsCtor)
    {
      //
      // traverse the subtypes
      CtorType /*!*/
        rawCtorType = rawType.AsCtor;
      List<Type> /*!*/
        newArguments = new List<Type>();
      foreach (Type /*!*/ subtype in rawCtorType.Arguments.ToList())
      {
        Contract.Assert(subtype != null);
        newArguments.Add(ThinOutType(subtype, boundTypeParams,
          instantiations));
      }

      return new CtorType(Token.NoToken, rawCtorType.Decl, newArguments);
      //
    }
    else
    {
      System.Diagnostics.Debug.Fail("Don't know how to handle this type: " + rawType);
      return rawType; // compiler appeasement policy
    }
  }
}