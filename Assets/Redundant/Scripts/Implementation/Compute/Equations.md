<h1>The math equations used
<h2>Distance:

$$
d = \sqrt{(x_1 - x_2)^2 + (y_1 - y_2)^2 + (z_1 - z_2)^2}
$$

Where $x_1$ is pos1 and $x_2$ is pos2. It represents the distance from $x_2$ to $x_1$
<h2> Distance2:

$$
d = (x_1 - x_2)^2 + (y_1 - y_2)^2 + (z_1 - z_2)^2
$$

<h2>Poly6 smoothing kernel:

$$
W_{\text{poly}6}(r, h) =
\begin{cases}
\dfrac{315}{64\pi h^9} \left(h^2 - r^2\right)^3, & 0 \le r \le h \\
0 & \text{otherwise}
\end{cases}
$$

Where r is the distance and h is the smoothing radius