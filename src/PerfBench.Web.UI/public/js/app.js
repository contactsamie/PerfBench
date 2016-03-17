import 'babel-polyfill'
import {createStore, combineReducers, applyMiddleware, compose} from 'redux'
import thunk from 'redux-thunk'
import React, {PropTypes} from 'react'
import {connect, Provider} from 'react-redux'
import {render} from 'react-dom'
import PureRenderMixin from 'react-addons-pure-render-mixin';

let websocket
let wsUri = "ws://localhost:8083/websocket"
let output

const event = (e, action) => {
    switch (action.type) {
        case 'StartedEvent':
            return {
                id: action.id,
                text: action.text,
                type: action.type
            }
        case 'FinishedEvent':
        case 'FailedEvent':
            if (!e) {
                return {
                    id: action.id,
                    text: action.text,
                    type: action.type
                }
            }
            if (e.id === action.id) {
                return {...e, type: action.type, text: action.text}
            }
            return e;
        default:
            return e
    }
}

const events = (state = [], action) => {
    //console.log("state: " + JSON.stringify(state))
    switch (action.type) {
        case 'StartedEvent':
            if (state.find(e => e.id === action.id)) return state
            return [
                ...state,
                event(undefined, action)
            ]
        case 'FinishedEvent':
        case 'FailedEvent':
            if (!state.find(e => e.id === action.id)) {
                return [
                    ...state,
                    event(undefined, action)
                ]
            }
            return state.map(e =>
                event(e, action)
            )
        default:
            return state
    }
}

const eventsReducers = combineReducers({
    events
})

function startedEvent(_id, text) {
    return {
        type: 'StartedEvent',
        id: _id,
        text
    }
}

function finishedEvent(_id, text) {
    return {
        type: 'FinishedEvent',
        id: _id,
        text
    }
}

function failedEvent(_id, text) {
    return {
        type: 'FailedEvent',
        id: _id,
        text
    }
}

class StreamingEvent extends React.Component {
    constructor(props) {
        super(props);
        this.shouldComponentUpdate = PureRenderMixin.shouldComponentUpdate.bind(this);
    }

    render() {
        //console.log("Rendering - " + JSON.stringify(this.props))
        let {type, text} = this.props
        return (
            <li className={"flex-item " + type} title={text}>
            </li>
        )
    }
}

StreamingEvent.propTypes = {
    type: PropTypes.string.isRequired,
    text: PropTypes.number.isRequired
}

const StreamingEventList = ({events}) =>
    (
        <ul className="flex-container">
            {events.map(event =>
                <StreamingEvent
                    key={event.id}
                    {...event}
                />
            )}
        </ul>
    )

// StreamingEventList.propTypes = {
//   events: PropTypes.arrayOf(PropTypes.shape({
//     id: PropTypes.number.isRequired,
//     text: PropTypes.string.number,
//     type: PropTypes.string.isRequired
//   }).isRequired).isRequired,
// }

const Header = ({average, max}) => (
    <ul>
        <li>Average: {average}</li>
        <li>Maximum: {max}</li>
    </ul>
)

// Header.propTypes = {
//   average: PropTypes.number.isRequired,
//   max: PropTypes.number.isRequired
// }

const mapStateToProps = (state) => {
    //console.log("STATE:" + JSON.stringify(state.events))
    return {
        events: state.events
    }
}

const mapStateToHeaderProps = (state) => {
    let f = state.events.filter(e => e.type == "FinishedEvent")
    let finished = f.map(e => parseFloat(e.text))
    return {
        average: finished.reduce((a, b)=>a + b, 0) / finished.length,
        max: Math.max.apply(Math, finished)
    }
}

const VisibleHeader = connect(
    mapStateToHeaderProps
)(Header)

const VisibleEventList = connect(
    mapStateToProps
)(StreamingEventList)

const App = () => (
    <div>
        <VisibleHeader />
        <VisibleEventList />
    </div>
)

function getNumericId(item) {
    let s = item.split("-")
    return parseInt(s.pop())
}

function dispatchEvent(event) {
    switch (event.Case) {
        case 'StartedEvent':
            //console.log("Started " + getNumericId(event.Item))
            store.dispatch(startedEvent(getNumericId(event.Item), 0.0))
            break;
        case 'FinishedEvent':
            //console.log("Finished " + getNumericId(event.Item1))
            store.dispatch(finishedEvent(getNumericId(event.Item1), parseFloat(event.Item3)))
            break;
        case 'FailedEvent':
            store.dispatch(failedEvent(getNumericId(event.Item1), parseFloat(event.Item3)))
            break;
        default:
            //console.log("Unknown event")
            break;
    }
}

const finalCreateStore = compose(
    applyMiddleware(thunk),
    //window.devToolsExtension ? window.devToolsExtension() : f => f
)(createStore);

let store = finalCreateStore(eventsReducers)

render(
    <Provider store={store}>
        <App />
    </Provider>,
    document.getElementById('react')
)

function init() {
    output = document.getElementById("output");
    testWebSocket();
}
function testWebSocket() {
    websocket = new WebSocket(wsUri);
    websocket.onopen = onOpen
    websocket.onclose = onClose
    websocket.onmessage = onMessage
    websocket.onerror = onError
}
function onOpen(evt) {
    //console.log(evt)
    writeToScreen("CONNECTED");
    doSend("INIT");
}
function onClose(evt) {
    writeToScreen("DISCONNECTED");
}

function onMessage(evt) {
    let data = JSON.parse(evt.data)
    if (data.value.contents) {
        //let reversed = data.value.contents.reverse();
        data.value.contents.forEach(e => dispatchEvent(e[0]))
    } else {
        dispatchEvent(data.value)
    }
}
function onError(evt) {
    writeToScreen('<span style="color: red;">ERROR:</span> ' + evt.data);
}
function doSend(message) {
    writeToScreen("SENT: " + message);
    websocket.send(message);
}
function writeToScreen(message) {
    var pre = document.createElement("p");
    pre.style.wordWrap = "break-word";
    pre.innerHTML = message;
    output.appendChild(pre);
}
window.addEventListener("load", init, false);
