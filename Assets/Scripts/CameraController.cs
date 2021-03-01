using UnityEngine;
using Unity.Mathematics;

[RequireComponent( typeof( Camera ) )]
public class CameraController : MonoBehaviour
{
    [SerializeField] private float _mouseSense = 1.8f;
    [SerializeField] private float _translationSpeed = 15f;
    [SerializeField] private float _movementSpeed = 0.5f;
    [SerializeField] private float _boostedSpeed = 20f;
    [SerializeField] private float _keyboardRotationSpeed = 0.5f;

    private bool zoomToLocation = false;
    private Vector3 zoomLocation = Vector3.zero;

    private Vector3 deltaPosition = Vector3.zero;
    private float currentSpeed = 0f;

    private void Update()
    {
        transform.Translate( Vector3.forward * Input.mouseScrollDelta.y * Time.deltaTime * _translationSpeed );

        deltaPosition = Vector3.zero;
        currentSpeed = _movementSpeed;

        if ( Input.GetKey( KeyCode.LeftShift ) )
        {
            currentSpeed = _boostedSpeed;
            zoomToLocation = false;
        }
        if ( Input.GetKey( KeyCode.W ) )
        {
            deltaPosition += transform.forward;
            zoomToLocation = false;
        }
        if ( Input.GetKey( KeyCode.S ) )
        {
            deltaPosition -= transform.forward;
            zoomToLocation = false;
        }
        if ( Input.GetKey( KeyCode.A ) )
        {
            deltaPosition -= transform.right;
            zoomToLocation = false;
        }
        if ( Input.GetKey( KeyCode.D ) )
        {
            deltaPosition += transform.right;
            zoomToLocation = false;
        }

        if ( Input.GetKey( KeyCode.Mouse2 ) )
        {
            zoomToLocation = false;

            // Pitch
            transform.rotation *= Quaternion.AngleAxis(
                -Input.GetAxis( "Mouse Y" ) * _mouseSense ,
                Vector3.right
            );

            // Paw
            transform.rotation = Quaternion.Euler(
                transform.eulerAngles.x ,
                transform.eulerAngles.y + Input.GetAxis( "Mouse X" ) * _mouseSense ,
                transform.eulerAngles.z
            );
        }
        else
        {
            if ( Input.GetKey( KeyCode.Q ) )
            {
                transform.Rotate( Vector3.down * _keyboardRotationSpeed , Space.World );
                zoomToLocation = false;
            }
            if ( Input.GetKey( KeyCode.E ) )
            {
                transform.Rotate( Vector3.up * _keyboardRotationSpeed , Space.World );
                zoomToLocation = false;
            }
            if ( Input.GetKey( KeyCode.T ) )
            {
                transform.Rotate( Vector3.left * _keyboardRotationSpeed , Space.World );
                zoomToLocation = false;
            }
            if ( Input.GetKey( KeyCode.G ) )
            {
                transform.Rotate( Vector3.right * _keyboardRotationSpeed , Space.World );
                zoomToLocation = false;
            }
        }

        if ( zoomToLocation )
        {
            transform.position = Vector3.Lerp( transform.position , zoomLocation , 0.125f );

            if ( math.distance( transform.position , zoomLocation ) <= 0.125f )
            {
                zoomToLocation = false;
            }
        }
        else
        {
            transform.position += deltaPosition * currentSpeed;
        }
    }

    public void ZoomToLocation( Vector3 location )
    {
        transform.LookAt( location );
        zoomLocation = new Vector3( location.x , transform.position.y , location.z );
        zoomToLocation = true;
    }
}
