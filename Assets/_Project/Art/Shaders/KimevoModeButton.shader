// Boton circular de modo: anillo, relleno liquido neon, glow e icono animado.
//
// Todo el dibujo es procedural y todo el movimiento vive en el shader. Es una decision de
// presupuesto, no de elegancia: animar esto desde C# significaria tocar propiedades de un
// Graphic cada frame, y cualquier cambio en un Graphic marca su vertice como sucio y encola
// una reconstruccion del Canvas. Con la barra animandose en bucle eso seria un Canvas.Rebuild
// por frame compitiendo con el render AR, que es exactamente lo que el brief prohibe. Aqui la
// CPU solo escribe unos pocos floats cuando el estado CAMBIA; el bucle continuo lo lleva la
// GPU leyendo _Time.
//
// Las formas son todas analiticas (circulos, segmentos, arcos). Se descarto el trazo por
// muestreo de polilinea, que se ve mejor pero cuesta una decena de iteraciones por pixel, y
// esto tiene que correr en un moto g35 sin robarle frames a ARCore.
Shader "KIMEVO/ModeButton"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Neon ("Color neon del modo", Color) = (0.13, 0.91, 0.88, 1)
        _Ink ("Color del halo oscuro", Color) = (0.02, 0.03, 0.05, 1)

        _Activation ("Activacion 0-1", Range(0,1)) = 0
        _Press ("Pulsacion 0-1", Range(0,1)) = 0
        _Disabled ("Deshabilitado 0-1", Range(0,1)) = 0
        _IconId ("Icono: 0 explorar 1 colocar 2 dibujar", Float) = 0
        _Motion ("Cantidad de animacion 0-1", Range(0,1)) = 1
        _Seed ("Desfase de fase", Float) = 0

        // Requeridas por uGUI aunque no se usen: sin ellas el Image no puede enmascararse
        // dentro de un RectMask2D y Unity avisa por consola.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _Neon;
            fixed4 _Ink;
            float _Activation;
            float _Press;
            float _Disabled;
            float _IconId;
            float _Motion;
            float _Seed;

            static const float PI = 3.14159265;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            // Antialiasing en anchura de pixel. fwidth da cuanto cambia el valor entre pixeles
            // vecinos, asi que el borde sale igual de suave a cualquier resolucion sin tener
            // que pasarle el tamano del boton al shader.
            float aa(float d)
            {
                float w = max(fwidth(d), 1e-5);
                return saturate(0.5 - d / w);
            }

            // Composicion "over" estandar, no premultiplicada.
            //
            // Se escribe explicita porque lo facil - mezclar el color con lerp y quedarse con
            // el maximo de los alfas - no es composicion sino una aproximacion que se lava en
            // cuanto hay tres o cuatro capas. Con disco, halo, liquido, anillo e icono
            // apilados, la diferencia entre hacerlo bien y hacerlo a ojo es la diferencia
            // entre un boton que se ve sobre una pared blanca y uno que no.
            void over(inout float4 dst, float3 c, float a)
            {
                float outA = a + dst.a * (1.0 - a);
                dst.rgb = outA > 1e-5 ? (c * a + dst.rgb * dst.a * (1.0 - a)) / outA : dst.rgb;
                dst.a = outA;
            }

            // Distancia con signo a un segmento. Es la primitiva de la que salen el icono de
            // colocar y la base del de dibujar.
            float sdSegment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            // Anillo de radio r: distancia al borde, no al centro.
            float sdRing(float2 p, float r)
            {
                return abs(length(p) - r);
            }

            // ---------------------------------------------------------------- iconos

            // Explorar: ondas concentricas que se expanden desde el centro y se desvanecen.
            // Tres a la vez, desfasadas un tercio de ciclo, que es lo que hace que el bucle
            // se lea continuo en vez de como un pulso que empieza y acaba.
            float iconExplore(float2 p, float t)
            {
                float acc = 0;

                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    float phase = frac(t * 0.45 + i / 3.0);
                    float r = 0.06 + phase * 0.30;
                    float fade = 1.0 - phase;
                    acc += aa(sdRing(p, r) - 0.013) * fade * fade;
                }

                acc += aa(length(p) - 0.035);
                return saturate(acc);
            }

            // Colocar: un punto que baja hacia una linea base y rebota. La caida usa t*t
            // (acelera, como caeria de verdad) y el rebote se amortigua; un movimiento lineal
            // aqui se lee como un ascensor, no como algo que se posa.
            float iconPlace(float2 p, float t)
            {
                float baseY = -0.17;
                float cycle = frac(t * 0.42);

                float y;
                if (cycle < 0.62)
                {
                    float k = cycle / 0.62;
                    y = lerp(0.22, baseY, k * k);
                }
                else
                {
                    float k = (cycle - 0.62) / 0.38;
                    // Rebote amortiguado: media onda de seno que decae.
                    y = baseY + sin(k * PI) * 0.085 * (1.0 - k);
                }

                // Ojo: 'line' es palabra reservada en HLSL (tipo de primitiva de geometria).
                float baseBar = aa(sdSegment(p, float2(-0.20, baseY), float2(0.20, baseY)) - 0.014);
                float marker = aa(length(p - float2(0, y)) - 0.055);
                return saturate(baseBar + marker);
            }

            // Dibujar: un arco que se traza y se borra en bucle, con la punta marcada. El
            // trazo se define por angulo y no por muestreo de una curva, asi que cuesta un
            // atan2 en vez de una decena de iteraciones.
            float iconDraw(float2 p, float t)
            {
                float cycle = frac(t * 0.30);

                // Primera mitad del ciclo dibuja, segunda borra por la cola.
                float head = saturate(cycle < 0.5 ? cycle / 0.5 : 1.0);
                float tail = cycle < 0.5 ? 0.0 : (cycle - 0.5) / 0.5;

                float r = 0.20;
                float ang = atan2(p.y, p.x);

                // Normalizado a 0-1 sobre un arco de 300 grados que empieza abajo a la
                // izquierda. El 0.5 del final centra el recorrido visualmente.
                float u = (ang + PI * 0.833) / (PI * 1.666);

                float onArc = step(tail, u) * step(u, head) * step(0.0, u) * step(u, 1.0);
                float stroke = aa(sdRing(p, r) - 0.015) * onArc;

                // La punta: donde esta escribiendo ahora mismo.
                float headAng = -PI * 0.833 + head * PI * 1.666;
                float2 headPos = float2(cos(headAng), sin(headAng)) * r;
                float tip = aa(length(p - headPos) - 0.038) * step(cycle, 0.5);

                return saturate(stroke + tip);
            }

            // Candado para el estado no disponible. Cuerpo mas arco, sin animacion: lo que
            // esta bloqueado no debe respirar como lo que esta vivo.
            float iconLock(float2 p)
            {
                // El cuerpo tiene que ser mas ancho que el arco pero no mucho mas, y el arco
                // tiene que nacer EN el borde superior del cuerpo. Con el arco flotando por
                // encima y mas estrecho de la cuenta, el candado se leia abierto, que es
                // justo el significado contrario al que queremos.
                float2 q = p - float2(0, -0.055);
                float2 d = abs(q) - float2(0.105, 0.075);
                float body = aa(length(max(d, 0.0)) + min(max(d.x, d.y), 0.0));

                float shackleY = 0.020;
                float shackle = aa(sdRing(p - float2(0, shackleY), 0.078) - 0.017)
                                * step(0.0, p.y - shackleY);

                return saturate(body + shackle);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Espacio centrado: -0.5 a 0.5 en ambos ejes.
                float2 p = IN.texcoord - 0.5;
                float t = _Time.y * _Motion + _Seed;

                float radius = 0.40;
                float d = length(p) - radius;

                // Al pulsar el circulo entero encoge un 6%. Se hace escalando el espacio y no
                // el RectTransform porque tocar el transform de un Graphic vuelve a ensuciar
                // el Canvas, que es justo lo que este shader existe para evitar.
                float press = 1.0 - _Press * 0.06;
                p /= press;
                d = length(p) - radius;

                float inside = aa(d);
                float ring = aa(abs(d) - 0.018);

                // ---- relleno liquido
                // La superficie no es plana: dos senos de distinta frecuencia y sentido dan un
                // vaiven que no se repite a simple vista. Con uno solo se ve el bucle.
                float wave = sin(p.x * 16.0 + t * 2.2) * 0.020
                           + sin(p.x * 9.0 - t * 1.5) * 0.014;

                // El nivel va de fuera por abajo a fuera por arriba, para que la activacion
                // llene el circulo entero y no se quede a medias.
                float level = lerp(-radius - 0.10, radius + 0.10, _Activation);
                float liquid = aa(p.y - level - wave * _Activation) * inside;

                // ---- glow exterior
                float glow = exp(-max(d, 0.0) * 16.0) * _Activation * 0.85;

                // ---- icono
                float icon;
                if (_IconId < 0.5)      icon = iconExplore(p, t);
                else if (_IconId < 1.5) icon = iconPlace(p, t);
                else                    icon = iconDraw(p, t);

                icon = lerp(icon, iconLock(p), _Disabled);

                // ---------------------------------------------------------------- composicion

                float4 col = float4(0, 0, 0, 0);

                // 1. Disco oscuro de base.
                //
                // Es lo que hace legible todo lo demas. El primer intento no lo tenia y sobre
                // un fondo claro los botones en reposo desaparecian: un trazo claro al 40%
                // sobre blanco no es un trazo, es una sugerencia. Dibujar el circulo mas
                // brillante habria arreglado el fondo claro y roto el oscuro.
                //
                // Un disco oscuro tenue resuelve los dos a la vez y encima es fiel a la
                // referencia de diseno: sobre negro no se ve, porque ya es negro.
                // Los alfas de aqui abajo parecen altos y lo son a proposito. El proyecto
                // trabaja en espacio LINEAL, y ahi la intuicion de alfa enganya: una mezcla al
                // 50% de tinta sobre un fondo claro no da un gris medio, da 0.70 en pantalla,
                // porque la mezcla ocurre sobre valores linealizados donde el fondo claro pesa
                // muchisimo mas. Se probaron 0.34 y 0.50 y en los dos casos el boton en reposo
                // se disolvia sobre blanco. 0.62 es el valor que da un disco de verdad gris.
                float disc = inside * lerp(0.62, 0.28, _Activation);
                over(col, _Ink.rgb, disc);

                // 2. Halo del borde: refuerza el contorno contra fondos muy claros.
                over(col, _Ink.rgb, aa(abs(d) - 0.044) * 0.92);

                // 3. Liquido. Va debajo del anillo y del icono para que ambos se sigan
                //    leyendo con el circulo lleno.
                over(col, _Neon.rgb, liquid * 0.88);

                // 4. Anillo: tenue en reposo, neon al activarse.
                float ringAlpha = lerp(0.42, 1.0, _Activation);
                ringAlpha = lerp(ringAlpha, 0.16, _Disabled);
                float3 ringCol = lerp(float3(0.91, 0.93, 0.95), _Neon.rgb, _Activation);
                over(col, ringCol, ring * ringAlpha);

                // 5. Icono: claro sobre el vacio, oscuro sobre el liquido. Asi mantiene
                //    contraste durante toda la transicion y no se pierde a medio llenado.
                float3 iconCol = lerp(float3(0.93, 0.95, 0.97), _Ink.rgb, liquid);
                float iconAlpha = lerp(lerp(0.80, 1.0, _Activation), 0.24, _Disabled);
                over(col, iconCol, icon * iconAlpha);

                // 6. Glow al final y aditivo, para sumarse al borde en vez de taparlo.
                col.rgb += _Neon.rgb * glow;
                col.a = saturate(col.a + glow * 0.5);

                col *= IN.color;
                return col;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
