// DigitalRune Engine - Copyright (C) DigitalRune GmbH
// This file is subject to the terms and conditions defined in
// file 'LICENSE.TXT', which is part of this source code package.

using System;
using DigitalRune.Mathematics.Algebra;


namespace DigitalRune.Mathematics.Interpolation
{
  /// <summary>
  /// Scattered Interpolation using multiple regression analysis with radial basis functions
  /// (single-precision).
  /// </summary>
  /// <remarks>
  /// <para>
  /// Radial basis functions are described in the paper "Pose Space Deformation: A Unified Approach 
  /// to Shape Interpolation and Skeleton-Driven Deformation".
  /// </para>
  /// <para>
  /// The regression can numerically fail for bad reference data pairs or an inappropriate basis 
  /// function. In this case <see cref="ScatteredInterpolationF.Setup"/> will throw a 
  /// <see cref="MathematicsException"/>.
  /// </para>
  /// </remarks>
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
  public class RadialBasisRegressionF : ScatteredInterpolationF
  {
    // TODO: We could also choose a unique basis function for each reference data pair.


    //--------------------------------------------------------------
    #region Fields
    //--------------------------------------------------------------

    private VectorF[] _weights;
    #endregion


    //--------------------------------------------------------------
    #region Properties
    //--------------------------------------------------------------

    /// <summary>
    /// Gets or sets the basis function.
    /// </summary>
    /// <value>
    /// <para>
    /// The basis function. The first parameter is the distance of the new data point <i>x</i> to 
    /// the reference data point <i>x<sub>i</sub></i>, which has been computed by the distance 
    /// function. The second parameter is the index <i>i</i> of the reference data point 
    /// <i>x<sub>i</sub></i>. (The second parameter is ignored by default. The same basis function 
    /// is applied for all reference data points. However, the index could be used to select a 
    /// unique basis function for each reference data point.)
    /// </para>
    /// <para>
    /// The default function is the Gaussian function (see 
    /// <see cref="MathHelper.Gaussian(float, float, float, float)"/>) with the parameters k = 1, 
    /// μ = 0, σ = 1.
    /// </para>
    /// </value>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    public Func<float, int, float> BasisFunction
    {
      get { return _basisFunction; }
      set 
      {
        if (value == null)
          throw new ArgumentNullException("value");

        _basisFunction = value; 
      }
    }
    private Func<float, int, float> _basisFunction = (x, i) => MathHelper.Gaussian(x, 1, 0, 1);


    /// <summary>
    /// Gets or sets the distance function.
    /// </summary>
    /// <value>
    /// <para>
    /// The distance function that computes the distance between a new data point <i>x</i> and the 
    /// reference data point <i>x<sub>i</sub></i>. The first parameter is the new data point 
    /// <i>x</i>. The second parameter is the reference data point <i>x<sub>i</sub></i>. The third 
    /// parameter is the index <i>i</i> of the reference data point <i>x<sub>i</sub></i>. (The third 
    /// parameter can be ignored in most cases.)
    /// </para>
    /// <para>
    /// The default distance function computes the Euclidean distance between <i>x</i> and 
    /// <i>x<sub>i</sub></i>.
    /// </para>
    /// <para>
    /// (The distance function could be generalized to the Mahalanobis distance.)
    /// </para>
    /// </value>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    public Func<VectorF, VectorF, int, float> DistanceFunction
    {
      get { return _distanceFunction; }
      set
      {
        if (value == null)
          throw new ArgumentNullException("value");

        _distanceFunction = value;
      }
    }
    private Func<VectorF, VectorF, int, float> _distanceFunction = (x, xi, i) => (x - xi).Length;
    #endregion


    //--------------------------------------------------------------
    #region Creation and Cleanup
    //--------------------------------------------------------------
    #endregion


    //--------------------------------------------------------------
    #region Methods
    //--------------------------------------------------------------

    /// <summary>
    /// Called when <see cref="ScatteredInterpolationF.Setup"/> is called.
    /// </summary>
    /// <remarks>
    /// Here internal values can be computed from the registered reference pairs if required. It is
    /// assured that the reference data pairs have valid dimensions: All x values have the same
    /// number of elements and all y values have the same number of elements. All reference data
    /// values are not <see langword="null"/>.
    /// </remarks>
    /// <exception cref="MathematicsException">
    /// Cannot compute regression - try to choose different reference data pairs or another basis 
    /// function.
    /// </exception>
    protected override void OnSetup()
    {
      // Compute weights: 
      // Make matrix Φ (also known as G).
      int numberOfPairs = Count;
      MatrixF G = new MatrixF(numberOfPairs, numberOfPairs);
      for (int r = 0; r < numberOfPairs; r++)  // rows
      {        
        for (int c = 0; c < numberOfPairs; c++)  // columns
        {
          float radialArgument = DistanceFunction(GetX(r), GetX(c), c);
          G[r, c] = BasisFunction(radialArgument, c);
        }
      }

      // We look for the matrix W that contains the weights.
      // Each row resembles an n-dimensional weight.
      MatrixF W;

      // Make the matrix Y with the reference values, such that ideally Y = G * W.
      MatrixF Y = new MatrixF(numberOfPairs, GetY(0).NumberOfElements);
      for (int r = 0; r < numberOfPairs; r++)
        Y.SetRow(r, GetY(r));

      // Compute W as the least squares solution.
      try
      {
        W = MatrixF.SolveLinearEquations(G, Y);
      }
      catch (MathematicsException exception)
      {
        throw new MathematicsException("Cannot compute regression - try to choose different reference data pairs or another basis function.", exception);
      }
      
      // The rows of W are the weights.
      _weights = new VectorF[numberOfPairs];
      for (int r = 0; r < numberOfPairs; r++)
        _weights[r] = W.GetRow(r);
    }


    /// <summary>
    /// Called when <see cref="ScatteredInterpolationF.Compute"/> is called.
    /// </summary>
    /// <param name="x">The x value.</param>
    /// <returns>The y value.</returns>
    /// <remarks>
    /// When this method is called, <see cref="ScatteredInterpolationF.Setup"/> has already been
    /// executed. And the parameter <paramref name="x"/> is not <see langword="null"/>.
    /// </remarks>
    protected override VectorF OnCompute(VectorF x)
    {
      // Compute result as weighted sum.
      VectorF y = new VectorF(GetY(0).NumberOfElements);
      var numberOfPairs = Count;
      for (int i = 0; i < numberOfPairs; i++)
      {
        float radialArgument = DistanceFunction(x, GetX(i), i);
        y += _weights[i]*BasisFunction(radialArgument, i);
      }

      return y;
    }
    #endregion
  } 
}
