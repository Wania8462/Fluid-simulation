<h1> The math equations used

<h2> Distance:

$$
d = \sqrt{(x_1 - x_2)^2 + (y_1 - y_2)^2 + (z_1 - z_2)^2}
$$

Where $x_1$ is pos1 and $x_2$ is pos2. It represents the distance from $x_2$ to $x_1$
<h2> DistanceSq:

$$
d = (x_1 - x_2)^2 + (y_1 - y_2)^2 + (z_1 - z_2)^2
$$

<h2> Quadratic spiky kernel:

$$
W_{ij} = (1 - r_{ij} / h)^2
$$

<h2>Poly6 smoothing kernel:

$$
W_{\text{poly}6}(r, h) =
\begin{cases}
\dfrac{315}{64\pi h^9} \left(h^2 - r^2\right)^3, & 0 \le r \le h \\
0 & \text{otherwise}
\end{cases}
$$

<h2> All formulas implemented in `FluidMath.cs` (2D versions)

<h3>Distance (2D)</h3>
$$
d(\mathbf{p}_1,\mathbf{p}_2)=\sqrt{(x_2-x_1)^2+(y_2-y_1)^2}
$$

<h3>Squared distance (2D)</h3>
$$
d^2(\mathbf{p}_1,\mathbf{p}_2)=(x_2-x_1)^2+(y_2-y_1)^2
$$

<h3>Unit vector</h3>
For two points $\mathbf{p}_1,\mathbf{p}_2$ and distance $d$: 
$$
\hat{\mathbf{u}}_{12}=\frac{\mathbf{p}_2-\mathbf{p}_1}{d(\mathbf{p}_1,\mathbf{p}_2)}
$$
An overload uses a precomputed $d$:
$$
\hat{\mathbf{u}}_{12}=\frac{\mathbf{p}_2-\mathbf{p}_1}{d}
$$

<h3>Pressure displacement</h3>
Let $r$ be the relative distance (called `relativeDistance` in code), and define $\alpha=1-r$. Then the displacement applied due to pressure is:
$$
\Delta\mathbf{x}_{\text{pressure}}=\Delta t^2\left( P\,\alpha + P_{near}\,\alpha^2 + P_{border}\,\alpha\right)\,\hat{\mathbf{u}}_{ij}
$$
where $P$ is `pseudoPressure`, $P_{near}$ is `nearPseudoPressure`, $P_{border}$ is `borderPressure`, $\Delta t$ is `deltaTime`, and $\hat{\mathbf{u}}_{ij}$ is the unit vector between particles.

<h3>Viscosity impulse</h3>
With inward relative velocity $v_{in}$ and viscosities $\mu_{high},\mu_{low}$:
$$
\Delta\mathbf{v}_{\text{viscosity}}=\Delta t\,(1-r)\,(\mu_{high}+\mu_{low})\,v_{in}\,\hat{\mathbf{u}}_{ij}
$$
(the code sums each viscosity multiplied by the inward velocity)

<h3>Stretch spring</h3>
For a stretching spring the scalar displacement contribution is:
$$
S_{stretch}=\Delta t\,\beta\,(m - L_0 - d_{def})
$$
where $\beta$ is `plasticity`, $m$ is `magnitude`, $L_0$ is `restLength`, and $d_{def}$ is `deformation`.

<h3>Compress spring</h3>
For compression:
$$
S_{compress}=\Delta t\,\beta\,(L_0 - d_{def} - m)
$$

<h3>Displacement by spring</h3>
For the vector displacement contributed by a spring:
$$
\Delta\mathbf{x}_{spring}=\Delta t^2\,k_s\left(1-\frac{L_0}{R}\right)(L_0 - m)\,\hat{\mathbf{u}}_{ij}
$$
where $k_s$ is `springStiffness`, $L_0$ is `springRestLength`, $R$ is `interactionRadius`, and $m$ is `magnitude`.

<h3>Quadratic spiky kernel (relative distance)</h3>
If $q$ denotes `relativeDistance` (i.e., $q = r/h$):
$$
W_{spiky}^{(2)}(q)=(1-q)^2
$$

<h3>Cubic spiky kernel</h3>
$$
W_{spiky}^{(3)}(q)=(1-q)^3
$$

<h3>Poly6 implementation note</h3>
The code implements a poly6-like kernel using the squared radius difference and explicit powers:
$$
W_{poly6}(r,h)=\frac{315}{64\pi h^9}\,(h^2-r^2)^3\quad\text{for }0\le r\le h
$$
In code the numerator is computed as $315\cdot|h^2-r^2|^3$ and the denominator as $64\pi h^9$.

<h3>Helper powers</h3>
$$
		ext{Pow3}(x)=x^3,
\qquad
	ext{Pow9}(x)=x^9
$$

---

If you want, I can also add brief notes mapping each symbol to the exact parameter names in `FluidMath.cs`.