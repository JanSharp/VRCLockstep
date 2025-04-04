
[To index](index.md)

# Target Audience

Lockstep is oriented towards experienced programmers. This is not to say that newer programmers cannot use lockstep, however it is quickly going to get difficult. There are only a few parts of lockstep that are beginner friendly, and if it turns out that some system requires [latency hiding](latency-states.md) then it simply is not easy.

The majority of C# concepts below should be well understood:

- type
  - class
  - struct
  - enum
- inheritance
  - base
  - derive
- member
  - property
    - getter
    - setter
  - field
  - method/function
    - parameter
      - out
      - ref
    - argument
    - return
- reference
- attribute
- annotation
- intellisense
- visibility
  - public
  - private
  - protected
- abstract
- overload
- override
- control flow

And other important things to know:

- UdonSharp
  - public method == custom Udon event. Relates to SendCustomEvent and friends
- Unity serialization (component/prefab/scene)
  - public == serialized by default
  - non public == non serialized by default
  - SerializeField attribute
  - System.NonSerialized attribute
- HideInInspector attribute
